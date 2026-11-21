using Core.Primitives;
using VpnManager.Core.Configuration;
using VpnManager.Core.Model;
using VpnManager.Core.NetworkManager;
using VpnManager.Core.Presentation;
using VpnManager.Core.Runtime;
using VpnManager.Core.Services;
using VpnManager.Core.Tests.Fakes;

namespace VpnManager.Core.Tests;

/// <summary>
/// The presenter owns every decision; these tests drive it with a recording view
/// and assert both the decisions and the redaction boundary.
/// </summary>
public sealed class TunnelListPresenterTests
{
    [Fact]
    public async Task Initialising_shows_the_rows_and_a_status_line()
    {
        var context = CreateContext();
        context.Store.Files["wg0"] = TestData.SampleConfigText;

        await context.Presenter.InitializeAsync();

        var row = Assert.Single(context.View.Tunnels);
        Assert.Equal("wg0", row.Name);
        Assert.Equal("Down", row.State);
        Assert.Equal("2 peers", row.Peers);
        Assert.Equal("configured", row.Configuration);
        Assert.NotNull(context.View.Status);
        Assert.Null(context.View.Error);
    }

    [Fact]
    public async Task Opening_the_editor_never_sends_the_private_key_to_the_view()
    {
        var context = CreateContext();
        context.Store.Files["wg0"] = TestData.SampleConfigText;

        await context.Presenter.BeginEditAsync("wg0");

        var editor = context.View.Editor;
        Assert.NotNull(editor);
        Assert.Equal("wg0", editor.Name);
        Assert.False(editor.NameEditable);
        Assert.Equal("10.10.0.11/32", editor.Addresses);
        Assert.Equal("1420", editor.Mtu);
        Assert.Equal("51820", editor.ListenPort);
        Assert.True(editor.HasPrivateKey);
        Assert.Equal(2, editor.Peers.Count);
        Assert.Equal(TestData.DerivedPublicKey, Assert.Single(context.View.KeyUpdates).PublicKey);
        Assert.False(context.View.ReceivedTextContaining(TestData.InterfacePrivateKey));
    }

    [Fact]
    public async Task Opening_the_editor_for_a_missing_configuration_is_an_error()
    {
        var context = CreateContext();

        await context.Presenter.BeginEditAsync("wg9");

        Assert.NotNull(context.View.Error);
        Assert.Null(context.View.Editor);
    }

    [Fact]
    public async Task Creating_and_saving_writes_a_parseable_configuration()
    {
        var context = CreateContext();

        context.Presenter.BeginCreate();
        Assert.True(context.View.EditorVisible);
        Assert.True(context.View.Editor!.IsNew);

        await context.Presenter.SaveAsync(CreateInput(privateKey: TestData.InterfacePrivateKey));

        Assert.False(context.View.EditorVisible);
        var write = Assert.Single(context.Store.Writes);
        Assert.Equal("wg3", write.Name);

        var reparsed = WireGuardConfigParser.Parse("wg3", write.Contents);
        Assert.True(reparsed.IsSuccess);
        Assert.Equal(TestData.InterfacePrivateKey, reparsed.Value.Interface.PrivateKey.Reveal());
        Assert.Equal("10.10.0.3/32", Assert.Single(reparsed.Value.Interface.Addresses));
    }

    [Fact]
    public async Task Saving_an_invalid_field_marks_that_field_and_writes_nothing()
    {
        var context = CreateContext();
        context.Presenter.BeginCreate();

        await context.Presenter.SaveAsync(CreateInput(name: string.Empty, privateKey: TestData.InterfacePrivateKey));

        Assert.Contains(context.View.EditorErrors, entry => entry.StartsWith(EditorField.Name, StringComparison.Ordinal));
        Assert.Empty(context.Store.Writes);
        Assert.True(context.View.EditorVisible);
    }

    [Fact]
    public async Task Saving_a_non_numeric_mtu_marks_the_mtu_field()
    {
        var context = CreateContext();
        context.Presenter.BeginCreate();

        await context.Presenter.SaveAsync(CreateInput(privateKey: TestData.InterfacePrivateKey, mtu: "big"));

        Assert.Contains(context.View.EditorErrors, entry => entry.StartsWith(EditorField.Mtu, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Generating_a_key_pair_sends_only_the_public_key_to_the_view()
    {
        var context = CreateContext();
        context.Presenter.BeginCreate();

        await context.Presenter.GenerateKeyPairAsync();

        var update = Assert.Single(context.View.KeyUpdates);
        Assert.True(update.HasPrivateKey);
        Assert.Equal(TestData.DerivedPublicKey, update.PublicKey);
        Assert.False(context.View.ReceivedTextContaining(TestData.InterfacePrivateKey));
    }

    [Fact]
    public async Task A_generated_key_is_persisted_by_the_next_save()
    {
        var context = CreateContext();
        context.Presenter.BeginCreate();
        await context.Presenter.GenerateKeyPairAsync();

        await context.Presenter.SaveAsync(CreateInput(privateKey: string.Empty));

        var write = Assert.Single(context.Store.Writes);
        var reparsed = WireGuardConfigParser.Parse("wg3", write.Contents);
        Assert.True(reparsed.IsSuccess);
        Assert.Equal(TestData.InterfacePrivateKey, reparsed.Value.Interface.PrivateKey.Reveal());
        Assert.False(context.View.ReceivedTextContaining(TestData.InterfacePrivateKey));
    }

    [Fact]
    public void Adding_and_removing_peer_rows_is_delegated_to_the_view()
    {
        var context = CreateContext();
        context.Presenter.BeginCreate();

        context.View.RequestAddPeer();
        Assert.Single(context.View.AppendedPeers);

        context.View.RequestRemovePeer(0);
        Assert.Equal([0], context.View.RemovedPeerIndexes);

        context.View.RequestRemovePeer(5);
        Assert.Equal([0], context.View.RemovedPeerIndexes);
    }

    [Fact]
    public void A_peer_row_can_be_added_with_values()
    {
        var context = CreateContext();
        context.Presenter.BeginCreate();

        context.Presenter.AddPeer(new EditorPeerInput("label", TestData.PeerOneKey, string.Empty, "0.0.0.0/0", string.Empty, "25"));

        Assert.Equal("label", Assert.Single(context.View.AppendedPeers).Label);
    }

    [Fact]
    public void Cancelling_closes_the_editor()
    {
        var context = CreateContext();
        context.Presenter.BeginCreate();

        context.View.RequestCancel();

        Assert.False(context.View.EditorVisible);
    }

    [Fact]
    public async Task Deleting_a_tunnel_refreshes_the_list()
    {
        var context = CreateContext();
        context.Store.Files["wg0"] = TestData.SampleConfigText;

        await context.Presenter.DeleteAsync("wg0");

        Assert.False(context.Store.Files.ContainsKey("wg0"));
        Assert.Null(context.View.Error);
    }

    [Fact]
    public async Task Deleting_a_running_tunnel_reports_the_refusal()
    {
        var context = CreateContext();
        context.Store.Files["wg0"] = TestData.SampleConfigText;
        context.Inspector.InterfaceNames.Add("wg0");

        await context.Presenter.DeleteAsync("wg0");

        Assert.NotNull(context.View.Error);
        Assert.Contains("stop it", context.View.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Importing_uses_the_file_stem_as_the_tunnel_name()
    {
        var context = CreateContext();
        context.Store.External["/tmp/wg7.conf"] = TestData.SampleConfigText;

        await context.Presenter.ImportAsync("/tmp/wg7.conf");

        Assert.True(context.Store.Files.ContainsKey("wg7"));
        Assert.Null(context.View.Error);
    }

    [Fact]
    public async Task Importing_an_unusable_file_is_shown_as_an_error()
    {
        var context = CreateContext();
        context.Store.External["/tmp/incoming.conf"] = "not a configuration";

        await context.Presenter.ImportAsync("/tmp/incoming.conf");

        Assert.NotNull(context.View.Error);
    }

    [Fact]
    public async Task Exporting_without_a_selection_is_refused()
    {
        var context = CreateContext();

        await context.Presenter.RefreshAsync();
        await context.Presenter.ExportAsync("/tmp/exported.conf");

        Assert.NotNull(context.View.Error);
        Assert.Empty(context.Store.Exports);
    }

    [Fact]
    public async Task Exporting_the_selected_tunnel_writes_the_file()
    {
        var context = CreateContext();
        context.Store.Files["wg0"] = TestData.SampleConfigText;
        await context.Presenter.SelectAsync("wg0");

        await context.Presenter.ExportAsync("/tmp/exported.conf");

        Assert.Null(context.View.Error);
        Assert.Equal(TestData.SampleConfigText, Assert.Single(context.Store.Exports).Contents);
    }

    [Fact]
    public async Task The_refresh_event_reaches_the_presenter()
    {
        var context = CreateContext();
        context.Store.Files["wg0"] = TestData.SampleConfigText;

        context.View.RequestRefresh();

        await (context.Presenter.PendingOperation ?? Task.CompletedTask);
        Assert.Single(context.View.Tunnels);
    }

    [Fact]
    public async Task The_selected_tunnel_event_reaches_the_presenter()
    {
        var context = CreateContext();
        context.Store.Files["wg0"] = TestData.SampleConfigText;

        context.View.SelectTunnel("wg0");

        await Task.CompletedTask;
        Assert.Equal("wg0", context.Presenter.SelectedTunnel);
    }

    [Fact]
    public async Task A_null_input_is_a_programming_error()
    {
        var context = CreateContext();

        await Assert.ThrowsAsync<ArgumentNullException>(() => context.Presenter.SaveAsync(null!));
    }

    private static EditorInput CreateInput(
        string name = "wg3",
        string privateKey = "",
        string addresses = "10.10.0.3/32",
        string listenPort = "",
        string mtu = "",
        params EditorPeerInput[] peers)
    {
        var rows = peers.Length > 0
            ? peers
            : [new EditorPeerInput("peer-one", TestData.PeerOneKey, string.Empty, "0.0.0.0/0", string.Empty, string.Empty)];

        return new EditorInput(name, privateKey, addresses, string.Empty, listenPort, mtu, string.Empty, TunnelRoutingMode.Full, rows);
    }

    private static WireGuardInterface LiveInterface() => new()
    {
        Name = "wg0",
        PublicKey = TestData.PeerOneKey,
        ListenPort = 51820,
        Peers =
        [
            new WireGuardPeer
            {
                PublicKey = TestData.PeerTwoKey,
                Endpoint = "vpn.example.com:51820",
                AllowedIPs = ["10.10.0.0/24"],
                LatestHandshakeText = "1 minute, 20 seconds ago",
                LatestHandshakeAge = TimeSpan.FromSeconds(80),
                Received = ByteSize.FromBytes(1239),
                Sent = ByteSize.FromBytes(3670016),
                PersistentKeepalive = 25,
            },
        ],
    };

    private static Context CreateContext()
    {
        var store = new FakeWireGuardConfigStore();
        var inspector = new FakeWireGuardRuntimeInspector();
        var service = new TunnelService(store, inspector);
        var view = new FakeTunnelView();
        var presenter = new TunnelListPresenter(view, service);
        return new Context(presenter, view, store, inspector);
    }

    private sealed record Context(
        TunnelListPresenter Presenter,
        FakeTunnelView View,
        FakeWireGuardConfigStore Store,
        FakeWireGuardRuntimeInspector Inspector);
}

/// <summary>The editor text to domain mapping.</summary>
public sealed class EditorInputMapperTests
{
    [Fact]
    public void Lists_split_on_newlines_and_commas()
    {
        Assert.Equal(["a", "b", "c"], EditorInputMapper.SplitList("a\nb, c"));
        Assert.Empty(EditorInputMapper.SplitList("   \n , "));
        Assert.Empty(EditorInputMapper.SplitList(null));
    }

    [Fact]
    public void A_valid_input_becomes_a_valid_configuration()
    {
        var input = new EditorInput(
            "wg0",
            TestData.InterfacePrivateKey,
            "10.10.0.11/32\n10.10.0.12/32",
            "10.10.0.1",
            "51820",
            "1420",
            "auto",
            TunnelRoutingMode.Split,
            [new EditorPeerInput("peer", TestData.PeerOneKey, TestData.PresharedKey, "0.0.0.0/0, ::/0", "vpn.example.com:51820", "25")]);

        var result = EditorInputMapper.ToConfiguration(input, SecretKey.Empty);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);
        Assert.Equal(["10.10.0.11/32", "10.10.0.12/32"], result.Value.Interface.Addresses);
        Assert.Equal(51820, result.Value.Interface.ListenPort);
        Assert.Equal("auto", result.Value.Interface.Table);
        Assert.Equal(TestData.PresharedKey, result.Value.Peers[0].PresharedKey!.Reveal());
    }

    [Fact]
    public void A_blank_typed_private_key_keeps_the_one_the_presenter_holds()
    {
        var input = new EditorInput("wg0", "   ", "10.0.0.1/32", string.Empty, string.Empty, string.Empty, string.Empty, TunnelRoutingMode.Split, []);

        var result = EditorInputMapper.ToConfiguration(input, SecretKey.FromValue(TestData.InterfacePrivateKey));

        Assert.True(result.IsSuccess);
        Assert.Equal(TestData.InterfacePrivateKey, result.Value.Interface.PrivateKey.Reveal());
    }

    [Fact]
    public void A_blank_preshared_key_keeps_the_resolved_one()
    {
        var input = new EditorInput(
            "wg0",
            TestData.InterfacePrivateKey,
            "10.0.0.1/32",
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            TunnelRoutingMode.Split,
            [new EditorPeerInput(string.Empty, TestData.PeerOneKey, string.Empty, "0.0.0.0/0", string.Empty, string.Empty)]);

        var result = EditorInputMapper.ToConfiguration(
            input,
            SecretKey.Empty,
            [SecretKey.FromValue(TestData.PresharedKey)]);

        Assert.True(result.IsSuccess);
        Assert.Equal(TestData.PresharedKey, result.Value.Peers[0].PresharedKey!.Reveal());
    }

    [Theory]
    [InlineData("ListenPort")]
    [InlineData("Mtu")]
    [InlineData("Keepalive")]
    public void A_non_numeric_field_is_refused_with_its_own_code(string field)
    {
        var peer = new EditorPeerInput(string.Empty, TestData.PeerOneKey, string.Empty, "0.0.0.0/0", string.Empty, field == "Keepalive" ? "soon" : string.Empty);
        var input = new EditorInput(
            "wg0",
            TestData.InterfacePrivateKey,
            "10.0.0.1/32",
            string.Empty,
            field == "ListenPort" ? "many" : string.Empty,
            field == "Mtu" ? "big" : string.Empty,
            string.Empty,
            TunnelRoutingMode.Split,
            [peer]);

        var result = EditorInputMapper.ToConfiguration(input, SecretKey.Empty);

        Assert.True(result.IsFailure);
        var expected = field switch
        {
            "ListenPort" => WireGuardErrorCodes.ListenPortInvalid,
            "Mtu" => WireGuardErrorCodes.MtuInvalid,
            _ => WireGuardErrorCodes.KeepaliveInvalid,
        };

        Assert.Equal(expected, result.Error.Code);
    }

    [Fact]
    public void The_field_key_of_every_validation_code_is_mapped()
    {
        Assert.Equal(EditorField.Name, EditorField.FromErrorCode(WireGuardErrorCodes.TunnelNameEmpty));
        Assert.Equal(EditorField.PrivateKey, EditorField.FromErrorCode(WireGuardErrorCodes.PrivateKeyInvalid));
        Assert.Equal(EditorField.Addresses, EditorField.FromErrorCode(WireGuardErrorCodes.AddressInvalid));
        Assert.Equal(EditorField.Dns, EditorField.FromErrorCode(WireGuardErrorCodes.DnsInvalid));
        Assert.Equal(EditorField.ListenPort, EditorField.FromErrorCode(WireGuardErrorCodes.ListenPortInvalid));
        Assert.Equal(EditorField.Mtu, EditorField.FromErrorCode(WireGuardErrorCodes.MtuInvalid));
        Assert.Equal(EditorField.PeerPublicKey, EditorField.FromErrorCode(WireGuardErrorCodes.KeyInvalid));
        Assert.Equal(EditorField.PeerPresharedKey, EditorField.FromErrorCode(WireGuardErrorCodes.PresharedKeyInvalid));
        Assert.Equal(EditorField.PeerAllowedIps, EditorField.FromErrorCode(WireGuardErrorCodes.AllowedIpsInvalid));
        Assert.Equal(EditorField.PeerEndpoint, EditorField.FromErrorCode(WireGuardErrorCodes.EndpointInvalid));
        Assert.Equal(EditorField.PeerKeepalive, EditorField.FromErrorCode(WireGuardErrorCodes.KeepaliveInvalid));
        Assert.Equal(EditorField.General, EditorField.FromErrorCode("config.not_found"));
    }

    [Fact]
    public async Task Polling_the_transfer_samples_every_listed_tunnel()
    {
        var view = new FakeTunnelView();
        var clock = new ManualTimeProvider();
        var reader = new FakeNetworkCounterReader();
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        store.Files["wg1"] = TestData.SampleConfigText;
        var service = new TunnelService(store, new FakeWireGuardRuntimeInspector());
        var presenter = new TunnelListPresenter(view, service, clock, counterReader: reader);

        reader.Counters["wg0"] = new NetworkCounters(100, 200);
        reader.Counters["wg1"] = new NetworkCounters(300, 400);
        await presenter.RefreshAsync();
        presenter.PollTransfer();

        Assert.Equal(2, view.TunnelTransfers.Count);
    }

    [Fact]
    public async Task Starting_or_stopping_a_read_only_service_reports_the_failure()
    {
        var view = new FakeTunnelView();
        var service = new TunnelService(new FakeWireGuardConfigStore(), new FakeWireGuardRuntimeInspector(), readOnly: true);
        var presenter = new TunnelListPresenter(view, service);

        await presenter.StartAsync("wg0");
        Assert.NotNull(view.Error);

        await presenter.StopAsync("wg0");
        Assert.NotNull(view.Error);
    }

    [Fact]
    public async Task A_down_tunnel_reports_zero_transfer()
    {
        var view = new FakeTunnelView();
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var service = new TunnelService(store, new FakeWireGuardRuntimeInspector());
        var presenter = new TunnelListPresenter(view, service, counterReader: new FakeNetworkCounterReader());

        await presenter.RefreshAsync();
        await presenter.SelectAsync("wg0");
        presenter.PollTransfer();

        var transfer = view.TunnelTransfers["wg0"];
        Assert.Equal("0 B/s", transfer.DownloadRate);
        Assert.Equal("0 B", transfer.DownloadTotal);
        Assert.Equal("0 B/s", transfer.UploadRate);
        Assert.Equal("0 B", transfer.UploadTotal);
        Assert.Equal("0 B/s", view.Transfer!.DownloadRate);
    }

    [Fact]
    public async Task A_stopped_tunnel_resets_a_previous_rate_to_zero()
    {
        var view = new FakeTunnelView();
        var clock = new ManualTimeProvider();
        var reader = new FakeNetworkCounterReader();
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var service = new TunnelService(store, new FakeWireGuardRuntimeInspector());
        var presenter = new TunnelListPresenter(view, service, clock, counterReader: reader);

        await presenter.RefreshAsync();
        reader.Counters["wg0"] = new NetworkCounters(100, 200);
        presenter.PollTransfer();
        clock.Advance(TimeSpan.FromSeconds(1));
        reader.Counters["wg0"] = new NetworkCounters(300, 400);
        presenter.PollTransfer();

        Assert.NotEqual("0 B/s", view.TunnelTransfers["wg0"].DownloadRate);

        reader.Counters.Remove("wg0");
        presenter.PollTransfer();

        Assert.Equal("0 B/s", view.TunnelTransfers["wg0"].DownloadRate);
        Assert.Equal("0 B", view.TunnelTransfers["wg0"].DownloadTotal);
    }

    [Fact]
    public async Task Renaming_stores_a_display_name_and_refreshes()
    {
        var view = new FakeTunnelView();
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var service = new TunnelService(store, new FakeWireGuardRuntimeInspector(), stateStore: new FakeApplicationStateStore());
        var presenter = new TunnelListPresenter(view, service);

        await presenter.RefreshAsync();
        view.RequestRename("wg0");

        Assert.True(view.RenameVisible);
        Assert.Equal("wg0", view.Rename!.CurrentDisplayName);

        view.SubmitRename(new TunnelRenameInput("wg0", "Work VPN"));
        await (presenter.PendingOperation ?? Task.CompletedTask);

        Assert.False(view.RenameVisible);
        Assert.Contains(view.Toasts, toast => toast.Contains("Work VPN", StringComparison.Ordinal));
        Assert.Equal("Work VPN", Assert.Single(view.Tunnels).ShownName);
    }

    [Fact]
    public async Task Cancelling_a_rename_changes_nothing()
    {
        var view = new FakeTunnelView();
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var service = new TunnelService(store, new FakeWireGuardRuntimeInspector(), stateStore: new FakeApplicationStateStore());
        var presenter = new TunnelListPresenter(view, service);

        await presenter.RefreshAsync();
        view.RequestRename("wg0");
        view.RequestRenameCancel();

        Assert.False(view.RenameVisible);
        Assert.Equal("wg0", Assert.Single(view.Tunnels).ShownName);
    }

    [Fact]
    public async Task Reconnecting_bounces_an_owned_tunnel_only_when_the_link_changed()
    {
        var view = new FakeTunnelView();
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var inspector = new FakeWireGuardRuntimeInspector();
        inspector.InterfaceNames.Add("wg0");
        var networkManager = new FakeNetworkManagerConnections();
        var service = new TunnelService(store, inspector, networkManager: networkManager);
        var link = new FakePhysicalLinkReader { Link = new PhysicalLink("wlan0", "192.168.1.1") };
        var presenter = new TunnelListPresenter(view, service, physicalLink: link);

        await presenter.RefreshAsync();

        Assert.False(await presenter.ReconnectAsync());
        Assert.False(await presenter.ReconnectAsync());
        Assert.Empty(networkManager.Deactivated);

        link.Link = new PhysicalLink("wlan0", "192.168.1.254");

        Assert.True(await presenter.ReconnectAsync());
        Assert.Equal(["wg0"], networkManager.Deactivated);
        Assert.Equal(["wg0"], networkManager.Activated);
    }

    [Fact]
    public async Task Reconnecting_never_touches_a_foreign_tunnel()
    {
        var view = new FakeTunnelView();
        var networkManager = new FakeNetworkManagerConnections();
        networkManager.Connections.Add(new NetworkManagerConnection("corp", "wg0", IsActive: true, Permissions: ["user:operator"]));
        var service = new TunnelService(
            new FakeWireGuardConfigStore(),
            new FakeWireGuardRuntimeInspector(),
            TunnelScope.User,
            networkManager: networkManager);
        var link = new FakePhysicalLinkReader { Link = new PhysicalLink("wlan0", "192.168.1.1") };
        var presenter = new TunnelListPresenter(view, service, physicalLink: link);

        await presenter.RefreshAsync();
        await presenter.ReconnectAsync();
        link.Link = new PhysicalLink("wlan0", "10.0.0.1");

        Assert.False(await presenter.ReconnectAsync());
        Assert.Empty(networkManager.Deactivated);
    }

    [Fact]
    public async Task Reconnecting_waits_while_the_link_is_down()
    {
        var view = new FakeTunnelView();
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var inspector = new FakeWireGuardRuntimeInspector();
        inspector.InterfaceNames.Add("wg0");
        var networkManager = new FakeNetworkManagerConnections();
        var service = new TunnelService(store, inspector, networkManager: networkManager);
        var link = new FakePhysicalLinkReader { Link = new PhysicalLink("wlan0", "192.168.1.1") };
        var presenter = new TunnelListPresenter(view, service, physicalLink: link);

        await presenter.RefreshAsync();
        await presenter.ReconnectAsync();

        link.Link = null;
        Assert.False(await presenter.ReconnectAsync());
        Assert.Empty(networkManager.Deactivated);

        link.Link = new PhysicalLink("wlan0", "10.0.0.1");
        Assert.True(await presenter.ReconnectAsync());
        Assert.Equal(["wg0"], networkManager.Deactivated);
    }

    [Fact]
    public async Task Reconnecting_backs_off_so_a_flapping_link_cannot_loop()
    {
        var view = new FakeTunnelView();
        var clock = new ManualTimeProvider();
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var inspector = new FakeWireGuardRuntimeInspector();
        inspector.InterfaceNames.Add("wg0");
        var networkManager = new FakeNetworkManagerConnections();
        var service = new TunnelService(store, inspector, networkManager: networkManager);
        var link = new FakePhysicalLinkReader { Link = new PhysicalLink("wlan0", "192.168.1.1") };
        var presenter = new TunnelListPresenter(view, service, clock, physicalLink: link);

        await presenter.RefreshAsync();
        await presenter.ReconnectAsync();

        link.Link = new PhysicalLink("wlan0", "192.168.1.254");
        Assert.True(await presenter.ReconnectAsync());
        Assert.Single(networkManager.Deactivated);

        link.Link = new PhysicalLink("wlan0", "192.168.1.1");
        Assert.False(await presenter.ReconnectAsync());
        Assert.Single(networkManager.Deactivated);

        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.True(await presenter.ReconnectAsync());
        Assert.Equal(2, networkManager.Deactivated.Count);
    }

    [Fact]
    public async Task Connecting_reports_the_dns_containment()
    {
        var view = new FakeTunnelView();
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var networkManager = new FakeNetworkManagerConnections();
        var dns = new FakeDnsStateReader { State = new DnsState(["10.2.0.1"], ["~."], true) };
        var service = new TunnelService(store, new FakeWireGuardRuntimeInspector(), networkManager: networkManager, dnsStateReader: dns);
        var presenter = new TunnelListPresenter(view, service);

        await presenter.StartAsync("wg0");

        Assert.Contains(
            view.Toasts,
            toast => toast.Contains("every name resolves through the tunnel", StringComparison.Ordinal));
    }

    private const string FullTunnelConfig = """
        [Interface]
        Address = 10.2.0.2/32
        PrivateKey = yAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBmk=

        [Peer]
        PublicKey = xTIBA5rboUvnH4htodjb6e697QjLERt1NAB4mZqp8Dg=
        AllowedIPs = 0.0.0.0/0
        Endpoint = 1.2.3.4:51820
        """;

    [Fact]
    public async Task Connecting_a_full_tunnel_warns_when_the_sysctl_is_missing()
    {
        var view = new FakeTunnelView();
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = FullTunnelConfig;
        var service = new TunnelService(
            store,
            new FakeWireGuardRuntimeInspector(),
            networkManager: new FakeNetworkManagerConnections());
        var presenter = new TunnelListPresenter(
            view,
            service,
            routingPrerequisite: new FakeRoutingPrerequisiteReader { Value = new RoutingPrerequisite(0, 1) });

        await presenter.StartAsync("wg0");

        Assert.Contains(view.Toasts, toast => toast.Contains("full-tunnel sysctl is missing", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Connecting_a_split_tunnel_does_not_warn_about_the_sysctl()
    {
        var view = new FakeTunnelView();
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var service = new TunnelService(
            store,
            new FakeWireGuardRuntimeInspector(),
            networkManager: new FakeNetworkManagerConnections());
        var presenter = new TunnelListPresenter(
            view,
            service,
            routingPrerequisite: new FakeRoutingPrerequisiteReader { Value = new RoutingPrerequisite(0, 1) });

        await presenter.StartAsync("wg0");

        Assert.DoesNotContain(view.Toasts, toast => toast.Contains("full-tunnel sysctl", StringComparison.Ordinal));
    }
}
