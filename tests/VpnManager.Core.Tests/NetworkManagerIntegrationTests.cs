using VpnManager.Core.Model;
using VpnManager.Core.NetworkManager;
using VpnManager.Core.Presentation;
using VpnManager.Core.Process;
using VpnManager.Core.Services;
using VpnManager.Core.Tests.Fakes;

namespace VpnManager.Core.Tests;

/// <summary>
/// The NetworkManager integration: the workstation product provisions the tunnel
/// as an <c>nmcli</c> connection, so the application must read and toggle that
/// source of truth rather than assume a wg-quick file.
/// </summary>
public sealed class NetworkManagerConnectionParserTests
{
    [Fact]
    public void The_connection_list_is_parsed_and_unescaped()
    {
        var output = "work-vpn:wireguard:no\nworkstation-enp4s0:802-3-ethernet:yes\na\\:b:wireguard:yes\n";

        var entries = NetworkManagerConnectionParser.ParseConnectionList(output);

        Assert.Equal(3, entries.Count);
        Assert.Equal(new NetworkManagerConnectionEntry("work-vpn", "wireguard", false), entries[0]);
        Assert.Equal(new NetworkManagerConnectionEntry("workstation-enp4s0", "802-3-ethernet", true), entries[1]);
        Assert.Equal("a:b", entries[2].Name);
        Assert.True(entries[2].IsActive);
    }

    [Fact]
    public void A_property_is_read_up_to_its_first_colon()
    {
        var output = "connection.permissions:user:alice\nconnection.interface-name:wg0\n";

        Assert.Equal("user:alice", NetworkManagerConnectionParser.ReadProperty(output, "connection.permissions"));
        Assert.Equal("wg0", NetworkManagerConnectionParser.ReadProperty(output, "connection.interface-name"));
        Assert.Null(NetworkManagerConnectionParser.ReadProperty(output, "connection.absent"));
    }

    [Fact]
    public void Permissions_are_split_and_empty_when_unset()
    {
        Assert.Equal(
            ["user:alice", "user:bob"],
            NetworkManagerConnectionParser.ReadPermissions("connection.permissions:user:alice,user:bob\n"));
        Assert.Empty(NetworkManagerConnectionParser.ReadPermissions("connection.interface-name:wg0\n"));
    }
}

/// <summary>The service and presenter over a fake NetworkManager.</summary>
public sealed class NetworkManagerIntegrationTests
{
    [Fact]
    public async Task A_NetworkManager_connection_is_listed_as_its_own_row()
    {
        var networkManager = new FakeNetworkManagerConnections();
        networkManager.Connections.Add(Connection("work-vpn", "wg0", active: false, "user:alice"));
        var service = Build(networkManager);

        var result = await service.ListTunnelsAsync();

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value);
        Assert.Equal("work-vpn", row.Name);
        Assert.Equal(TunnelSource.NetworkManager, row.Source);
        Assert.Equal(TunnelState.Down, row.State);
        Assert.Contains("NetworkManager", row.Note!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_profile_the_application_owns_is_not_marked_managed()
    {
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var networkManager = new FakeNetworkManagerConnections();
        networkManager.Connections.Add(Connection("wg0", "wg0", active: true, "user:alice"));
        var service = Build(networkManager, store);

        var result = await service.ListTunnelsAsync();

        var row = Assert.Single(result.Value);
        Assert.Equal("wg0", row.Name);
        Assert.Equal(TunnelState.Up, row.State);
        Assert.Equal(TunnelSource.Configuration, row.Source);
        Assert.Null(row.Note);
    }

    [Fact]
    public async Task A_connection_the_application_did_not_create_is_listed_as_managed()
    {
        var store = new FakeWireGuardConfigStore();
        var networkManager = new FakeNetworkManagerConnections();
        networkManager.Connections.Add(Connection("work-vpn", "wg0", active: false, "user:alice"));
        var service = Build(networkManager, store);

        var result = await service.ListTunnelsAsync();

        var row = Assert.Single(result.Value);
        Assert.Equal("work-vpn", row.Name);
        Assert.Equal(TunnelSource.NetworkManager, row.Source);
        Assert.Equal("Managed by NetworkManager", row.Note);
    }

    [Fact]
    public async Task Connecting_a_foreign_connection_activates_it_without_importing()
    {
        var store = new FakeWireGuardConfigStore();
        var networkManager = new FakeNetworkManagerConnections();
        networkManager.Connections.Add(Connection("work-vpn", "wg0", active: false, "user:alice"));
        var service = Build(networkManager, store, owner: "alice");

        var result = await service.StartAsync("work-vpn");

        Assert.True(result.IsSuccess);
        Assert.Empty(networkManager.Ensured);
        Assert.Equal("work-vpn", Assert.Single(networkManager.Activated));
    }

    [Fact]
    public async Task The_user_scope_lists_only_the_owned_connections()
    {
        var networkManager = new FakeNetworkManagerConnections();
        networkManager.Connections.Add(Connection("mine", "wg0", active: false, "user:alice"));
        networkManager.Connections.Add(Connection("theirs", "wg1", active: false, "user:bob"));
        var service = Build(networkManager, owner: "alice", scope: TunnelScope.User);

        var result = await service.ListTunnelsAsync();

        Assert.Equal("mine", Assert.Single(result.Value).Name);
    }

    [Fact]
    public async Task Starting_a_NetworkManager_connection_goes_through_NetworkManager()
    {
        var networkManager = new FakeNetworkManagerConnections();
        networkManager.Connections.Add(Connection("work-vpn", "wg0", active: false, "user:alice"));
        var runner = FakeWireGuardCommandRunner.Returning(string.Empty);
        var service = Build(networkManager);

        var result = await service.StartAsync("work-vpn");

        Assert.True(result.IsSuccess);
        Assert.Equal("work-vpn", Assert.Single(networkManager.Activated));
        Assert.Empty(runner.Commands);
    }

    [Fact]
    public async Task Stopping_a_NetworkManager_connection_goes_through_NetworkManager()
    {
        var networkManager = new FakeNetworkManagerConnections();
        networkManager.Connections.Add(Connection("work-vpn", "wg0", active: true, "user:alice"));
        var runner = FakeWireGuardCommandRunner.Returning(string.Empty);
        var service = Build(networkManager);

        var result = await service.StopAsync("work-vpn");

        Assert.True(result.IsSuccess);
        Assert.Equal("work-vpn", Assert.Single(networkManager.Deactivated));
        Assert.Empty(runner.Commands);
    }

    [Fact]
    public async Task Editing_a_NetworkManager_connection_is_refused()
    {
        var networkManager = new FakeNetworkManagerConnections();
        networkManager.Connections.Add(Connection("work-vpn", "wg0", active: false, "user:alice"));
        var service = Build(networkManager);
        await service.ListTunnelsAsync();

        var loaded = service.LoadConfiguration("work-vpn");

        Assert.True(loaded.IsFailure);
        Assert.Equal(WireGuardErrorCodes.NetworkManagerManaged, loaded.Error.Code);
    }

    [Fact]
    public async Task Deleting_a_NetworkManager_connection_is_refused()
    {
        var networkManager = new FakeNetworkManagerConnections();
        networkManager.Connections.Add(Connection("work-vpn", "wg0", active: false, "user:alice"));
        var store = new FakeWireGuardConfigStore();
        var service = Build(networkManager, store);

        var deleted = await service.DeleteAsync("work-vpn");

        Assert.True(deleted.IsFailure);
        Assert.Equal(WireGuardErrorCodes.NetworkManagerManaged, deleted.Error.Code);
        Assert.Empty(store.Files);
    }

    [Fact]
    public async Task The_presenter_labels_a_NetworkManager_row()
    {
        var networkManager = new FakeNetworkManagerConnections();
        networkManager.Connections.Add(Connection("work-vpn", "wg0", active: false, "user:alice"));
        var view = new FakeTunnelView();
        var presenter = new TunnelListPresenter(view, Build(networkManager));

        await presenter.InitializeAsync();

        var row = Assert.Single(view.Tunnels);
        Assert.Equal("work-vpn", row.Name);
        Assert.Equal("NetworkManager", row.Configuration);
        Assert.Equal(TunnelSource.NetworkManager, row.Source);
    }

    private static NetworkManagerConnection Connection(string name, string interfaceName, bool active, params string[] permissions) =>
        new(name, interfaceName, active, permissions);

    private static TunnelService Build(
        FakeNetworkManagerConnections networkManager,
        FakeWireGuardConfigStore? store = null,
        string? owner = null,
        TunnelScope scope = TunnelScope.System,
        FakeWireGuardRuntimeInspector? runtime = null) =>
        new(
            store ?? new FakeWireGuardConfigStore(),
            runtime ?? new FakeWireGuardRuntimeInspector(),
            scope,
            new FakeApplicationStateStore(),
            networkManager: networkManager,
            networkManagerOwner: owner);
}

/// <summary>The real adapter over a scripted process seam.</summary>
public sealed class NmcliNetworkManagerConnectionsTests
{
    [Fact]
    public async Task The_adapter_lists_only_the_wireguard_connections_with_their_details()
    {
        var runner = new FakeProcessRunner(request => IsListRequest(request)
            ? ProcessResult.Completed(0, "work-vpn:wireguard:no\nworkstation-enp4s0:802-3-ethernet:yes\n", string.Empty)
            : ProcessResult.Completed(0, "connection.permissions:user:alice\nconnection.interface-name:wg0\n", string.Empty));
        var connections = new NmcliNetworkManagerConnections(runner);

        var result = await connections.ListWireGuardConnectionsAsync();

        Assert.True(result.IsSuccess);
        var connection = Assert.Single(result.Value);
        Assert.Equal("work-vpn", connection.Name);
        Assert.Equal("wg0", connection.InterfaceName);
        Assert.False(connection.IsActive);
        Assert.True(connection.IsOwnedBy("alice"));
    }

    [Fact]
    public async Task The_connection_detail_is_read_once_and_cached()
    {
        var runner = new FakeProcessRunner(request => IsListRequest(request)
            ? ProcessResult.Completed(0, "work-vpn:wireguard:no\n", string.Empty)
            : ProcessResult.Completed(0, "connection.permissions:user:alice\nconnection.interface-name:wg0\n", string.Empty));
        var connections = new NmcliNetworkManagerConnections(runner);

        await connections.ListWireGuardConnectionsAsync();
        var afterFirst = runner.Requests.Count;

        await connections.ListWireGuardConnectionsAsync();

        // A passive refresh spawns only the list call, no per-connection detail.
        Assert.Equal(afterFirst + 1, runner.Requests.Count);
    }

    [Fact]
    public async Task Importing_or_deleting_a_connection_invalidates_its_cached_detail()
    {
        var runner = new FakeProcessRunner(request => IsListRequest(request)
            ? ProcessResult.Completed(0, "wg0:wireguard:no\n", string.Empty)
            : ProcessResult.Completed(0, "connection.permissions:user:alice\nconnection.interface-name:wg0\n", string.Empty));
        var connections = new NmcliNetworkManagerConnections(runner);

        await connections.ListWireGuardConnectionsAsync();
        await connections.EnsureAsync("wg0", "wg0", "/tmp/wg0.conf", "alice");
        runner.Requests.Clear();

        await connections.ListWireGuardConnectionsAsync();

        Assert.Contains(runner.Requests, request => !IsListRequest(request));
    }

    [Fact]
    public async Task Activating_passes_the_verb_and_the_connection_id()
    {
        var runner = new FakeProcessRunner(_ => ProcessResult.Completed(0, string.Empty, string.Empty));
        var connections = new NmcliNetworkManagerConnections(runner);

        var result = await connections.ActivateAsync("work-vpn");

        Assert.True(result.IsSuccess);
        var request = Assert.Single(runner.Requests);
        Assert.Equal("nmcli", request.FileName);
        Assert.Equal(["connection", "up", "work-vpn"], request.Arguments);
    }

    [Fact]
    public async Task An_unavailable_nmcli_is_a_value_not_an_exception()
    {
        var connections = new NmcliNetworkManagerConnections(FakeProcessRunner.NotStarted());

        var result = await connections.ListWireGuardConnectionsAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.NetworkManagerUnavailable, result.Error.Code);
    }

    [Fact]
    public async Task A_non_zero_nmcli_exit_is_reported_with_its_standard_error()
    {
        var runner = new FakeProcessRunner(_ => ProcessResult.Completed(4, string.Empty, "not authorized"));
        var connections = new NmcliNetworkManagerConnections(runner);

        var result = await connections.DeactivateAsync("work-vpn");

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.NetworkManagerFailed, result.Error.Code);
        Assert.Contains("not authorized", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ensuring_a_connection_deletes_an_owned_connection_imports_and_renames()
    {
        var runner = new FakeProcessRunner(request =>
        {
            var arguments = string.Join(' ', request.Arguments);
            if (arguments.Contains("connection show", StringComparison.Ordinal) &&
                !arguments.Contains("connection show wg0", StringComparison.Ordinal))
            {
                return ProcessResult.Completed(0, "wg0:wireguard:no\n", string.Empty);
            }

            if (arguments.Contains("connection show wg0", StringComparison.Ordinal))
            {
                return ProcessResult.Completed(
                    0,
                    "connection.permissions:user:alice\nconnection.interface-name:wg0\n",
                    string.Empty);
            }

            return ProcessResult.Completed(0, string.Empty, string.Empty);
        });
        var connections = new NmcliNetworkManagerConnections(runner);

        var result = await connections.EnsureAsync("wg0", "wg0", "/tmp/wg0.conf", "alice");

        Assert.True(result.IsSuccess);
        var commands = runner.Requests.Select(request => string.Join(' ', request.Arguments)).ToArray();
        Assert.Contains("connection delete wg0", commands);
        Assert.Contains(commands, command => command.Contains("connection import type wireguard file /tmp/wg0.conf", StringComparison.Ordinal));
        Assert.Contains(commands, command => command.Contains("connection modify wg0 connection.id wg0", StringComparison.Ordinal)
            && command.Contains("connection.permissions user:alice", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ipv4_only_profiles_do_not_set_ipv6_dns_properties()
    {
        var runner = new FakeProcessRunner(_ => ProcessResult.Completed(0, string.Empty, string.Empty));
        var connections = new NmcliNetworkManagerConnections(runner);

        var result = await connections.ApplyDnsAsync(
            "work-vpn",
            ["1.1.1.1"],
            ["corp.example"],
            NetworkManagerDnsMode.Split,
            includeIpv6: false);

        Assert.True(result.IsSuccess);
        var command = string.Join(' ', Assert.Single(runner.Requests).Arguments);
        Assert.Contains("ipv4.dns 1.1.1.1", command, StringComparison.Ordinal);
        Assert.Contains("ipv4.dns-search corp.example", command, StringComparison.Ordinal);
        Assert.DoesNotContain("ipv6.", command, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_connection_name_that_looks_like_an_option_is_refused_before_nmcli()
    {
        var runner = new FakeProcessRunner(_ => ProcessResult.Completed(0, string.Empty, string.Empty));
        var connections = new NmcliNetworkManagerConnections(runner);

        var result = await connections.ActivateAsync("-danger");

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.NetworkManagerInvalidConnection, result.Error.Code);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task Setting_autoconnect_passes_the_policy_to_nmcli()
    {
        var runner = new FakeProcessRunner(_ => ProcessResult.Completed(0, string.Empty, string.Empty));
        var connections = new NmcliNetworkManagerConnections(runner);

        var result = await connections.SetAutoConnectAsync("work-vpn", autoConnect: true, priority: 10);

        Assert.True(result.IsSuccess);
        var command = string.Join(' ', Assert.Single(runner.Requests).Arguments);
        Assert.Contains("connection modify work-vpn", command, StringComparison.Ordinal);
        Assert.Contains("connection.autoconnect yes", command, StringComparison.Ordinal);
        Assert.Contains("connection.autoconnect-priority 10", command, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Applying_dns_classifies_families_and_sets_the_policy()
    {
        var runner = new FakeProcessRunner(_ => ProcessResult.Completed(0, string.Empty, string.Empty));
        var connections = new NmcliNetworkManagerConnections(runner);

        var result = await connections.ApplyDnsAsync(
            "work-vpn",
            ["10.0.0.1", "2001:db8::1", "~corp.example"],
            ["search.example"],
            NetworkManagerDnsMode.Split);

        Assert.True(result.IsSuccess);
        var command = string.Join(' ', Assert.Single(runner.Requests).Arguments);
        Assert.Contains("ipv4.dns 10.0.0.1", command, StringComparison.Ordinal);
        Assert.Contains("ipv6.dns 2001:db8::1", command, StringComparison.Ordinal);
        Assert.Contains("~corp.example", command, StringComparison.Ordinal);
        Assert.Contains("search.example", command, StringComparison.Ordinal);
        Assert.Contains("ipv4.ignore-auto-dns yes", command, StringComparison.Ordinal);
        Assert.Contains("ipv4.dns-priority -50", command, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deleting_passes_the_connection_id()
    {
        var runner = new FakeProcessRunner(_ => ProcessResult.Completed(0, string.Empty, string.Empty));
        var connections = new NmcliNetworkManagerConnections(runner);

        var result = await connections.DeleteAsync("wg0");

        Assert.True(result.IsSuccess);
        Assert.Equal(["connection", "delete", "wg0"], Assert.Single(runner.Requests).Arguments);
    }

    [Fact]
    public async Task Starting_without_NetworkManager_is_refused()
    {
        var service = new TunnelService(new FakeWireGuardConfigStore(), new FakeWireGuardRuntimeInspector());

        var result = await service.StartAsync("wg0");

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.NetworkManagerUnavailable, result.Error.Code);
    }

    private static bool IsListRequest(ProcessRequest request) =>
        request.Arguments.Count == 5
        && request.Arguments[0] == "-t"
        && request.Arguments[2] == "NAME,TYPE,ACTIVE"
        && request.Arguments[3] == "connection"
        && request.Arguments[4] == "show";
}
