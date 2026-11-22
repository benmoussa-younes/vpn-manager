using Core.Results;
using VpnManager.Core.Model;
using VpnManager.Core.NetworkManager;
using VpnManager.Core.Presentation;
using VpnManager.Core.Process;
using VpnManager.Core.Runtime;
using VpnManager.Core.Services;
using VpnManager.Core.State;
using VpnManager.Core.Tests.Fakes;
using VpnManager.Core.Validation;

namespace VpnManager.Core.Tests;

/// <summary>
/// The branches the main suites leave: an endpoint form, a directory refusal, a
/// counter that is missing, a command that throws, and the presenter's refusals
/// and guarded failure. Each is a shipped path, so each is asserted.
/// </summary>
public sealed class BranchCoverageTests
{
    [Theory]
    [InlineData("[::1]", false)]
    [InlineData("[::1]:51820", true)]
    [InlineData("[::1]51820", false)]
    [InlineData("::1:51820", false)]
    [InlineData("vpn.example.com:51820", true)]
    [InlineData("vpn.example.com", false)]
    [InlineData("host:", false)]
    public void An_endpoint_is_validated_across_its_forms(string value, bool expected) =>
        Assert.Equal(expected, NetworkText.IsValidEndpoint(value));

    [Theory]
    [InlineData(WireGuardErrorCodes.TableInvalid, EditorField.Table)]
    [InlineData(WireGuardErrorCodes.FwMarkInvalid, EditorField.Table)]
    [InlineData(WireGuardErrorCodes.InterfaceNotFound, EditorField.General)]
    public void The_field_key_covers_the_routing_directives(string code, string field) =>
        Assert.Equal(field, EditorField.FromErrorCode(code));

    [Fact]
    public void Creating_a_directory_over_a_file_is_refused_as_a_value()
    {
        using var directory = new TemporaryDirectory();
        var fileSystem = TestFileSystems.SystemConfig();
        var file = Path.Combine(directory.Path, "a-file");
        File.WriteAllText(file, "content");

        var created = fileSystem.CreateDirectory(Path.Combine(file, "child"));

        Assert.False(created);
    }

    [Fact]
    public void A_missing_tx_counter_is_a_failure()
    {
        var fileSystem = new FakeConfigFileSystem();
        fileSystem.Files[Path.Combine("/sys/class/net", "wg0", "statistics", "rx_bytes")] = "1024";
        var reader = new SysfsNetworkCounterReader(fileSystem);

        var result = reader.Read("wg0");

        Assert.True(result.IsFailure);
        Assert.Contains("tx_bytes", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_io_failure_of_nmcli_is_unavailable_not_an_exception()
    {
        var connections = new NmcliNetworkManagerConnections(new ThrowingProcessRunner());

        var result = await connections.ListWireGuardConnectionsAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.NetworkManagerUnavailable, result.Error.Code);
    }

    [Fact]
    public async Task An_unparsable_configuration_is_noted_in_the_row()
    {
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = "this is not a WireGuard configuration";
        var service = new TunnelService(store, new FakeWireGuardRuntimeInspector());

        var result = await service.ListTunnelsAsync();

        Assert.True(result.IsSuccess);
        Assert.NotNull(Assert.Single(result.Value).Note);
    }

    [Fact]
    public async Task A_failed_networkmanager_listing_does_not_break_the_list()
    {
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var networkManager = new FakeNetworkManagerConnections { ListError = FakeWireGuardConfigStore.Denied("nmcli") };
        var service = Build(store, networkManager);

        var result = await service.ListTunnelsAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("wg0", Assert.Single(result.Value).Name);
    }

    [Fact]
    public void Updating_an_invalid_configuration_is_refused()
    {
        var service = Build(new FakeWireGuardConfigStore());

        var result = service.Update(TestData.SampleConfiguration() with { Name = string.Empty });

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.TunnelNameEmpty, result.Error.Code);
    }

    [Fact]
    public void Updating_when_the_existence_check_fails_is_refused()
    {
        var store = new FakeWireGuardConfigStore
        {
            ExistsError = new CoreError(WireGuardErrorCodes.ConfigListFailed, "the directory is unreadable"),
        };
        var service = Build(store);

        var result = service.Update(TestData.SampleConfiguration());

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.ConfigListFailed, result.Error.Code);
    }

    [Fact]
    public async Task An_unknown_state_is_labelled_unknown()
    {
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var runtime = new FakeWireGuardRuntimeInspector
        {
            NamesError = new CoreError(WireGuardErrorCodes.CommandFailed, "wg show failed"),
        };
        var view = new FakeTunnelView();
        var presenter = new TunnelListPresenter(view, Build(store, runtime: runtime));

        await presenter.InitializeAsync();

        Assert.Equal("Unknown", Assert.Single(view.Tunnels).State);
    }

    [Fact]
    public async Task Exporting_a_refused_destination_reports_it()
    {
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        store.ExistingExternal.Add("/tmp/out.conf");
        var view = new FakeTunnelView();
        var presenter = new TunnelListPresenter(view, Build(store));
        await presenter.InitializeAsync();
        await presenter.SelectAsync("wg0");

        await presenter.ExportAsync("/tmp/out.conf");

        Assert.NotNull(view.Error);
    }

    [Fact]
    public async Task Saving_a_conflicting_new_tunnel_marks_the_error()
    {
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var view = new FakeTunnelView();
        var presenter = new TunnelListPresenter(view, Build(store));
        presenter.BeginCreate();
        var input = new EditorInput(
            "wg0",
            TestData.InterfacePrivateKey,
            "10.10.0.11/32",
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            TunnelRoutingMode.Full,
            [new EditorPeerInput("peer", TestData.PeerOneKey, string.Empty, "0.0.0.0/0", string.Empty, string.Empty)]);

        await presenter.SaveAsync(input);

        Assert.NotEmpty(view.EditorErrors);
        Assert.Empty(store.Writes);
    }

    [Fact]
    public async Task An_unexpected_failure_is_reported_by_the_guard()
    {
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var view = new FakeTunnelView { ThrowOnShowTunnels = true };
        var presenter = new TunnelListPresenter(view, Build(store));

        view.RequestRefresh();
        await (presenter.PendingOperation ?? Task.CompletedTask);

        Assert.NotNull(view.Error);
        Assert.Contains("Unexpected failure", view.Error!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("latest handshake: , ago")]
    [InlineData("latest handshake: x minutes ago")]
    [InlineData("transfer: 1 ZiB received")]
    public void A_malformed_peer_property_stays_a_value(string propertyLine)
    {
        var text = $"""
            interface: wg0
              public key: {TestData.PeerOneKey}

            peer: {TestData.PeerTwoKey}
              allowed ips: 10.10.0.0/24
              {propertyLine}
            """;

        var parsed = WireGuardShowParser.ParseInterfaces(text);

        Assert.True(parsed.IsSuccess);
        Assert.Single(parsed.Value[0].Peers);
    }

    [Fact]
    public async Task A_networkmanager_detail_call_that_fails_is_reported()
    {
        var runner = new FakeProcessRunner(request => request.Arguments.Count == 5
            && request.Arguments[2] == "NAME,TYPE,ACTIVE"
            ? ProcessResult.Completed(0, "work-vpn:wireguard:no\n", string.Empty)
            : ProcessResult.Completed(1, string.Empty, "unknown connection"));
        var connections = new NmcliNetworkManagerConnections(runner);

        var result = await connections.ListWireGuardConnectionsAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.NetworkManagerFailed, result.Error.Code);
    }

    [Fact]
    public async Task The_real_process_runner_passes_the_environment_and_the_standard_input()
    {
        var runner = new SystemProcessRunner();

        var environment = await runner.RunAsync(new ProcessRequest(
            "/usr/bin/printenv",
            ["BITCRAFTS_TEST"],
            Environment: new Dictionary<string, string> { ["BITCRAFTS_TEST"] = "ok" }));

        Assert.Equal(0, environment.ExitCode);
        Assert.Equal("ok", environment.StandardOutput.Trim());

        var piped = await runner.RunAsync(new ProcessRequest("/usr/bin/cat", [], StandardInput: "piped-through"));

        Assert.Equal(0, piped.ExitCode);
        Assert.Equal("piped-through", piped.StandardOutput);
    }

    [Fact]
    public void Updating_a_remembered_tunnel_replaces_its_entry()
    {
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var state = new FakeApplicationStateStore
        {
            State = new ApplicationState
            {
                Tunnels =
                [
                    new TunnelStateEntry { Name = "wg0", Scope = TunnelScope.System, Verified = true },
                    new TunnelStateEntry { Name = "other", Scope = TunnelScope.System, Verified = true },
                ],
            },
        };
        var service = new TunnelService(
            store,
            new FakeWireGuardRuntimeInspector(),
            TunnelScope.System,
            state);

        var result = service.Update(TestData.SampleConfiguration());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, state.State.Tunnels.Count);
        Assert.Contains(state.State.Tunnels, entry => entry.Name == "wg0" && entry.Verified);
    }

    private static TunnelService Build(
        FakeWireGuardConfigStore store,
        FakeNetworkManagerConnections? networkManager = null,
        FakeWireGuardRuntimeInspector? runtime = null) =>
        new(
            store,
            runtime ?? new FakeWireGuardRuntimeInspector(),
            TunnelScope.System,
            new FakeApplicationStateStore(),
            networkManager: networkManager,
            networkManagerOwner: null);

    private sealed class ThrowingProcessRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default) =>
            Task.FromException<ProcessResult>(new IOException("nmcli is gone"));
    }
}
