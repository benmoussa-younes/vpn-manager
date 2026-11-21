using Core.Results;
using VpnManager.Core.Model;
using VpnManager.Core.NetworkManager;
using VpnManager.Core.Runtime;
using VpnManager.Core.Services;
using VpnManager.Core.Tests.Fakes;

namespace VpnManager.Core.Tests;

/// <summary>
/// The application operations, exercised entirely through fakes: no privileged
/// command runs and no configuration file outside memory is touched.
/// </summary>
public sealed class TunnelServiceTests
{
    [Fact]
    public async Task The_tunnel_list_merges_what_is_configured_with_what_is_running()
    {
        var context = CreateContext();
        context.Store.Files["wg0"] = TestData.SampleConfigText;
        context.Store.Files["wg2"] = TestData.SampleConfigText;
        context.Inspector.InterfaceNames.AddRange(["wg0", "wg1"]);

        var result = await context.Service.ListTunnelsAsync();

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);
        Assert.Equal(["wg0", "wg1", "wg2"], result.Value.Select(row => row.Name));

        var running = result.Value[0];
        Assert.Equal(TunnelState.Up, running.State);
        Assert.True(running.HasConfiguration);
        Assert.Equal(2, running.PeerCount);
        Assert.Equal(51820, running.ListenPort);
        Assert.Null(running.Note);

        var runtimeOnly = result.Value[1];
        Assert.Equal(TunnelState.Up, runtimeOnly.State);
        Assert.False(runtimeOnly.HasConfiguration);

        var configuredOnly = result.Value[2];
        Assert.Equal(TunnelState.Down, configuredOnly.State);
        Assert.True(configuredOnly.HasConfiguration);
    }

    [Fact]
    public async Task An_unreadable_configuration_directory_is_reported_per_row_and_does_not_hide_the_running_interfaces()
    {
        var context = CreateContext();
        context.Store.ListError = new CoreError(WireGuardErrorCodes.ConfigPermissionDenied, "denied for the test");
        context.Inspector.InterfaceNames.Add("wg0");

        var result = await context.Service.ListTunnelsAsync();

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value);
        Assert.Equal(TunnelState.Up, row.State);
        Assert.False(row.HasConfiguration);
        Assert.NotNull(row.Note);
        Assert.Contains("denied", row.Note!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task When_neither_source_can_be_read_the_list_is_a_failure()
    {
        var context = CreateContext();
        context.Store.ListError = new CoreError(WireGuardErrorCodes.ConfigPermissionDenied, "denied");
        context.Inspector.NamesError = new CoreError(WireGuardErrorCodes.ToolsUnavailable, "wg missing");

        var result = await context.Service.ListTunnelsAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.ConfigPermissionDenied, result.Error.Code);
    }

    [Fact]
    public async Task An_unreadable_configuration_file_is_noted_on_its_row()
    {
        var context = CreateContext();
        context.Store.Files["wg0"] = TestData.SampleConfigText;
        context.Store.DeniedReads.Add("wg0");

        var result = await context.Service.ListTunnelsAsync();

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value[0].Note);
    }

    [Fact]
    public void Creating_a_tunnel_writes_the_rendered_configuration()
    {
        var context = CreateContext();
        var configuration = TestData.SampleConfiguration() with { Name = "wg3" };

        var result = context.Service.Create(configuration);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);
        var write = Assert.Single(context.Store.Writes);
        Assert.Equal("wg3", write.Name);
        Assert.Contains("[Interface]", write.Contents, StringComparison.Ordinal);
    }

    [Fact]
    public void Creating_a_tunnel_that_already_exists_is_refused()
    {
        var context = CreateContext();
        context.Store.Files["wg0"] = TestData.SampleConfigText;

        var result = context.Service.Create(TestData.SampleConfiguration());

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.ConfigExists, result.Error.Code);
        Assert.Empty(context.Store.Writes);
    }

    [Fact]
    public void Creating_an_invalid_tunnel_is_refused_before_any_write()
    {
        var context = CreateContext();

        var result = context.Service.Create(TestData.SampleConfiguration() with { Name = string.Empty });

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.TunnelNameEmpty, result.Error.Code);
        Assert.Empty(context.Store.Writes);
    }

    [Fact]
    public void A_write_refused_by_the_file_system_is_a_value()
    {
        var context = CreateContext();
        context.Store.DeniedWrites.Add("wg3");

        var result = context.Service.Create(TestData.SampleConfiguration() with { Name = "wg3" });

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.ConfigPermissionDenied, result.Error.Code);
    }

    [Fact]
    public void Updating_replaces_an_existing_configuration()
    {
        var context = CreateContext();
        context.Store.Files["wg0"] = TestData.SampleConfigText;

        var result = context.Service.Update(TestData.SampleConfiguration());

        Assert.True(result.IsSuccess);
        Assert.Equal("wg0", Assert.Single(context.Store.Writes).Name);
    }

    [Fact]
    public void Updating_a_tunnel_that_does_not_exist_is_refused()
    {
        var context = CreateContext();

        var result = context.Service.Update(TestData.SampleConfiguration() with { Name = "wg9" });

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.ConfigNotFound, result.Error.Code);
    }

    [Fact]
    public async Task Deleting_removes_the_configuration()
    {
        var context = CreateContext();
        context.Store.Files["wg0"] = TestData.SampleConfigText;

        var result = await context.Service.DeleteAsync("wg0");

        Assert.True(result.IsSuccess);
        Assert.False(context.Store.Files.ContainsKey("wg0"));
    }

    [Fact]
    public async Task Deleting_a_running_tunnel_is_refused()
    {
        var context = CreateContext();
        context.Store.Files["wg0"] = TestData.SampleConfigText;
        context.Inspector.InterfaceNames.Add("wg0");

        var result = await context.Service.DeleteAsync("wg0");

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.TunnelAlreadyUp, result.Error.Code);
        Assert.True(context.Store.Files.ContainsKey("wg0"));
    }

    [Fact]
    public async Task Deleting_with_an_unusable_name_is_refused()
    {
        var context = CreateContext();

        var result = await context.Service.DeleteAsync("../wg0");

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.TunnelNameInvalid, result.Error.Code);
    }

    [Fact]
    public void Importing_parses_validates_and_stores_the_file()
    {
        var context = CreateContext();
        context.Store.External["/tmp/incoming.conf"] = TestData.SampleConfigText;

        var result = context.Service.Import("wg9", "/tmp/incoming.conf");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);
        Assert.True(context.Store.Files.ContainsKey("wg9"));
        Assert.Equal("wg9", Assert.Single(context.Store.Writes).Name);
    }

    [Fact]
    public void Importing_text_that_is_not_a_configuration_is_refused()
    {
        var context = CreateContext();
        context.Store.External["/tmp/incoming.conf"] = "this is not a configuration";

        var result = context.Service.Import("wg9", "/tmp/incoming.conf");

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.ImportInvalid, result.Error.Code);
        Assert.Empty(context.Store.Writes);
    }

    [Fact]
    public void Importing_a_configuration_that_is_not_valid_is_refused()
    {
        var context = CreateContext();
        context.Store.External["/tmp/incoming.conf"] = """
            [Interface]
            PrivateKey = short
            Address = nonsense
            """;

        var result = context.Service.Import("wg9", "/tmp/incoming.conf");

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.ImportInvalid, result.Error.Code);
    }

    [Fact]
    public void Importing_over_an_existing_name_is_refused()
    {
        var context = CreateContext();
        context.Store.Files["wg9"] = TestData.SampleConfigText;
        context.Store.External["/tmp/incoming.conf"] = TestData.SampleConfigText;

        var result = context.Service.Import("wg9", "/tmp/incoming.conf");

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.TunnelNameConflict, result.Error.Code);
    }

    [Fact]
    public void Importing_with_an_unusable_name_is_refused()
    {
        var context = CreateContext();
        context.Store.External["/tmp/incoming.conf"] = TestData.SampleConfigText;

        var result = context.Service.Import("a/b", "/tmp/incoming.conf");

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.TunnelNameInvalid, result.Error.Code);
    }

    [Fact]
    public void Importing_an_unreadable_source_is_refused()
    {
        var context = CreateContext();

        var result = context.Service.Import("wg9", "/tmp/absent.conf");

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Exporting_writes_the_stored_text_verbatim()
    {
        var context = CreateContext();
        context.Store.Files["wg0"] = TestData.SampleConfigText;

        var result = context.Service.Export("wg0", "/tmp/exported.conf");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);
        Assert.Equal(TestData.SampleConfigText, Assert.Single(context.Store.Exports).Contents);
    }

    [Fact]
    public void Exporting_over_an_existing_file_is_refused()
    {
        var context = CreateContext();
        context.Store.Files["wg0"] = TestData.SampleConfigText;
        context.Store.ExistingExternal.Add("/tmp/exported.conf");

        var result = context.Service.Export("wg0", "/tmp/exported.conf");

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.ExportFailed, result.Error.Code);
        Assert.Empty(context.Store.Exports);
    }

    [Fact]
    public void Exporting_a_tunnel_without_a_configuration_is_refused()
    {
        var context = CreateContext();

        var result = context.Service.Export("wg9", "/tmp/exported.conf");

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.ConfigNotFound, result.Error.Code);
    }

    [Fact]
    public async Task Key_generation_returns_a_redacted_key()
    {
        var context = CreateContext();

        var result = await context.Service.GeneratePrivateKeyAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("[redacted]", result.Value.ToString());
    }

    [Fact]
    public void The_service_exposes_the_directory_it_manages()
    {
        var context = CreateContext();

        Assert.Equal("/etc/wireguard", context.Service.ConfigDirectory);
    }

    private static Context CreateContext()
    {
        var store = new FakeWireGuardConfigStore();
        var inspector = new FakeWireGuardRuntimeInspector();
        var service = new TunnelService(store, inspector);
        return new Context(service, store, inspector);
    }

    [Fact]
    public async Task A_networkmanager_connection_without_a_configuration_is_a_foreign_row()
    {
        var networkManager = new FakeNetworkManagerConnections();
        networkManager.Connections.Add(new NetworkManagerConnection("vpn", "wg0", false, ["user:operator"]));
        var service = new TunnelService(
            new FakeWireGuardConfigStore(),
            new FakeWireGuardRuntimeInspector(),
            TunnelScope.User,
            networkManager: networkManager,
            networkManagerOwner: "operator");

        var result = await service.ListTunnelsAsync();

        Assert.True(result.IsSuccess);
        Assert.NotNull(Assert.Single(result.Value).Note);
    }

    [Fact]
    public async Task Deleting_without_a_networkmanager_only_touches_the_configuration()
    {
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var service = new TunnelService(store, new FakeWireGuardRuntimeInspector());

        var result = await service.DeleteAsync("wg0");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task The_tunnel_list_is_sorted_alphabetically_ignoring_case()
    {
        var store = new FakeWireGuardConfigStore();
        store.Files["alpha"] = TestData.SampleConfigText;
        store.Files["Beta"] = TestData.SampleConfigText;
        store.Files["gamma"] = TestData.SampleConfigText;
        var service = new TunnelService(store, new FakeWireGuardRuntimeInspector());

        var result = await service.ListTunnelsAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(["alpha", "Beta", "gamma"], result.Value.Select(row => row.Name));
    }

    [Fact]
    public async Task Connecting_a_second_tunnel_is_refused_while_another_is_up()
    {
        var networkManager = new FakeNetworkManagerConnections();
        networkManager.Connections.Add(new NetworkManagerConnection("vpnA", "wgA", IsActive: true, Permissions: ["user:operator"]));
        networkManager.Connections.Add(new NetworkManagerConnection("vpnB", "wgB", IsActive: false, Permissions: ["user:operator"]));
        var service = new TunnelService(
            new FakeWireGuardConfigStore(),
            new FakeWireGuardRuntimeInspector(),
            TunnelScope.User,
            networkManager: networkManager,
            networkManagerOwner: "operator");

        var result = await service.StartAsync("vpnB");

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.TunnelAlreadyConnected, result.Error.Code);
        Assert.Contains("vpnA", result.Error.Message, StringComparison.Ordinal);
        Assert.Empty(networkManager.Activated);
    }

    [Fact]
    public async Task Connecting_the_already_connected_tunnel_is_allowed()
    {
        var networkManager = new FakeNetworkManagerConnections();
        networkManager.Connections.Add(new NetworkManagerConnection("vpnA", "wgA", IsActive: true, Permissions: ["user:operator"]));
        var service = new TunnelService(
            new FakeWireGuardConfigStore(),
            new FakeWireGuardRuntimeInspector(),
            TunnelScope.User,
            networkManager: networkManager,
            networkManagerOwner: "operator");

        var result = await service.StartAsync("vpnA");

        Assert.True(result.IsSuccess);
        Assert.Equal(["vpnA"], networkManager.Activated);
    }

    [Fact]
    public async Task A_display_name_is_shown_and_can_be_cleared()
    {
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var service = new TunnelService(store, new FakeWireGuardRuntimeInspector(), stateStore: new FakeApplicationStateStore());

        var renamed = service.SetDisplayName("wg0", "Work VPN");
        Assert.True(renamed.IsSuccess);

        var listed = await service.ListTunnelsAsync();
        Assert.True(listed.IsSuccess);
        Assert.Equal("Work VPN", Assert.Single(listed.Value).DisplayName);

        var cleared = service.SetDisplayName("wg0", "   ");
        Assert.True(cleared.IsSuccess);
        Assert.Equal("wg0", cleared.Value);

        var relisted = await service.ListTunnelsAsync();
        Assert.True(relisted.IsSuccess);
        Assert.Null(Assert.Single(relisted.Value).DisplayName);
    }

    [Fact]
    public async Task The_list_sorts_by_the_display_name()
    {
        var store = new FakeWireGuardConfigStore();
        store.Files["zeta"] = TestData.SampleConfigText;
        store.Files["Alpha"] = TestData.SampleConfigText;
        var service = new TunnelService(store, new FakeWireGuardRuntimeInspector(), stateStore: new FakeApplicationStateStore());
        Assert.True(service.SetDisplayName("zeta", "Aardvark").IsSuccess);

        var result = await service.ListTunnelsAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(["zeta", "Alpha"], result.Value.Select(row => row.Name));
    }

    [Fact]
    public async Task A_display_name_too_long_is_refused()
    {
        var service = new TunnelService(
            new FakeWireGuardConfigStore(),
            new FakeWireGuardRuntimeInspector(),
            stateStore: new FakeApplicationStateStore());

        var result = service.SetDisplayName("wg0", new string('x', 65));

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.DisplayNameInvalid, result.Error.Code);
    }

    [Fact]
    public async Task The_dns_state_is_read_for_the_tunnels_interface()
    {
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var dns = new FakeDnsStateReader { State = new DnsState(["10.2.0.1"], ["~."], true) };
        var service = new TunnelService(store, new FakeWireGuardRuntimeInspector(), dnsStateReader: dns);

        var result = await service.DescribeDnsAsync("wg0");

        Assert.True(result.IsSuccess);
        Assert.Equal(DnsContainment.Contained, DnsContainmentPolicy.Classify(result.Value));
        Assert.Equal(["wg0"], dns.Read);
    }

    [Fact]
    public async Task The_dns_state_is_unavailable_without_a_reader()
    {
        var service = new TunnelService(new FakeWireGuardConfigStore(), new FakeWireGuardRuntimeInspector());

        var result = await service.DescribeDnsAsync("wg0");

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.DnsUnavailable, result.Error.Code);
    }

    private const string Ipv6CompleteConfig = """
        [Interface]
        PrivateKey = yAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBmk=
        Address = 10.2.0.2/32
        Address = 2a07:b944::2:2/128
        DNS = 10.2.0.1

        [Peer]
        PublicKey = xTIBA5rboUvnH4htodjb6e697QjLERt1NAB4mZqp8Dg=
        AllowedIPs = 0.0.0.0/0, ::/0
        Endpoint = 1.2.3.4:51820
        """;

    [Fact]
    public void The_ipv6_description_reports_the_profile_and_the_host()
    {
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = Ipv6CompleteConfig;
        var fileSystem = new FakeConfigFileSystem();
        fileSystem.Files["/proc/net/if_inet6"] = "fe800000000000000000000000000001 02 40 20 80 wlo1\n";
        var service = new TunnelService(store, new FakeWireGuardRuntimeInspector(), fileSystem: fileSystem);

        var result = service.DescribeIpv6("wg0");

        Assert.True(result.IsSuccess);
        Assert.Equal(Ipv6Completeness.Complete, result.Value.Profile);
        Assert.False(result.Value.HostHasIpv6);
    }

    [Fact]
    public async Task Connecting_imports_an_ipv4_only_copy_when_the_host_has_no_ipv6()
    {
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = Ipv6CompleteConfig;
        var networkManager = new FakeNetworkManagerConnections();
        var fileSystem = new FakeConfigFileSystem();
        fileSystem.Files["/proc/net/if_inet6"] = "fe800000000000000000000000000001 02 40 20 80 wlo1\n";
        var service = new TunnelService(
            store,
            new FakeWireGuardRuntimeInspector(),
            networkManager: networkManager,
            networkManagerOwner: "alice",
            fileSystem: fileSystem);

        var result = await service.StartAsync("wg0");

        Assert.True(result.IsSuccess);
        var export = Assert.Single(store.Exports);
        Assert.DoesNotContain("::/0", export.Contents, StringComparison.Ordinal);
        Assert.DoesNotContain("2a07", export.Contents, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Connecting_keeps_ipv6_when_the_host_has_it()
    {
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = Ipv6CompleteConfig;
        var networkManager = new FakeNetworkManagerConnections();
        var fileSystem = new FakeConfigFileSystem();
        fileSystem.Files["/proc/net/if_inet6"] = "20010db8000000000000000000000001 02 40 00 00 wlo1\n";
        var service = new TunnelService(
            store,
            new FakeWireGuardRuntimeInspector(),
            networkManager: networkManager,
            networkManagerOwner: "alice",
            fileSystem: fileSystem);

        var result = await service.StartAsync("wg0");

        Assert.True(result.IsSuccess);
        Assert.Contains("::/0", Assert.Single(store.Exports).Contents, StringComparison.Ordinal);
    }

    private sealed record Context(
        TunnelService Service,
        FakeWireGuardConfigStore Store,
        FakeWireGuardRuntimeInspector Inspector);
}
