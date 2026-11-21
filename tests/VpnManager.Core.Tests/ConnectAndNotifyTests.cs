using Core.Results;
using VpnManager.Core.Model;
using VpnManager.Core.NetworkManager;
using VpnManager.Core.Notifications;
using VpnManager.Core.Presentation;
using VpnManager.Core.Process;
using VpnManager.Core.Runtime;
using VpnManager.Core.Services;
using VpnManager.Core.State;
using VpnManager.Core.Tests.Fakes;

namespace VpnManager.Core.Tests;

/// <summary>A notifier that records what it was asked to show.</summary>
internal sealed class FakeNotifier : INotifier
{
    internal List<(string Title, string Body)> Notifications { get; } = [];

    public Task NotifyAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        Notifications.Add((title, body));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Connecting and disconnecting through NetworkManager, and the GNOME notification
/// the operator gets when the tunnel comes up or goes down.
/// </summary>
public sealed class ConnectAndNotifyTests
{
    [Fact]
    public async Task Connecting_imports_the_profile_and_activates_it()
    {
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var networkManager = new FakeNetworkManagerConnections();
        var service = Build(store, networkManager, owner: "alice");

        var result = await service.StartAsync("wg0");

        Assert.True(result.IsSuccess);
        Assert.Equal("wg0", Assert.Single(networkManager.Ensured));
        Assert.Equal("wg0", Assert.Single(networkManager.Activated));
        // The profile was rendered to a temporary file whose stem is the interface name.
        var export = Assert.Single(store.Exports);
        Assert.EndsWith("/wg0.conf", export.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Disconnecting_takes_the_connection_down()
    {
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var networkManager = new FakeNetworkManagerConnections();
        networkManager.Connections.Add(new NetworkManagerConnection("wg0", "wg0", true, ["user:alice"]));
        var service = Build(store, networkManager, owner: "alice");

        var result = await service.StopAsync("wg0");

        Assert.True(result.IsSuccess);
        Assert.Equal("wg0", Assert.Single(networkManager.Deactivated));
    }

    [Fact]
    public async Task A_profile_that_cannot_be_imported_is_not_activated()
    {
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var networkManager = new FakeNetworkManagerConnections
        {
            EnsureError = new CoreError(WireGuardErrorCodes.NetworkManagerFailed, "nmcli refused"),
        };
        var service = Build(store, networkManager, owner: "alice");

        var result = await service.StartAsync("wg0");

        Assert.True(result.IsFailure);
        Assert.Empty(networkManager.Activated);
    }

    [Fact]
    public async Task Starting_applies_the_default_autoconnect_and_dns_policy()
    {
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var networkManager = new FakeNetworkManagerConnections();
        var service = Build(store, networkManager, owner: "alice");

        var result = await service.StartAsync("wg0");

        Assert.True(result.IsSuccess);
        Assert.Equal(("wg0", true, 0), Assert.Single(networkManager.AutoConnectChanges));
        var dns = Assert.Single(networkManager.DnsChanges);
        Assert.Equal(NetworkManagerDnsMode.Profile, dns.Mode);
        Assert.Contains("10.10.0.1", dns.Dns);
    }

    [Fact]
    public async Task Starting_does_not_reapply_the_profile_it_is_about_to_activate()
    {
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var networkManager = new FakeNetworkManagerConnections();
        networkManager.Connections.Add(new NetworkManagerConnection("wg0", "wg0-if", true, ["user:alice"]));
        var service = Build(store, networkManager, owner: "alice");

        var result = await service.StartAsync("wg0");

        Assert.True(result.IsSuccess);
        Assert.Empty(networkManager.Reapplied);
    }

    [Fact]
    public async Task Stopping_marks_the_profile_as_manually_disconnected()
    {
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var networkManager = new FakeNetworkManagerConnections();
        networkManager.Connections.Add(new NetworkManagerConnection("wg0", "wg0", true, ["user:alice"]));
        var service = Build(store, networkManager, owner: "alice");

        var result = await service.StopAsync("wg0");

        Assert.True(result.IsSuccess);
        Assert.Equal(("wg0", false, 0), Assert.Single(networkManager.AutoConnectChanges));
    }

    [Fact]
    public async Task Setting_a_split_dns_policy_persists_and_applies_it()
    {
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var networkManager = new FakeNetworkManagerConnections();
        networkManager.Connections.Add(new NetworkManagerConnection("wg0", "wg0", false, ["user:alice"]));
        var service = Build(store, networkManager, owner: "alice");
        var policy = new TunnelPolicy(
            AutoReconnect: false,
            DnsMode: NetworkManagerDnsMode.Split,
            DnsSearchDomains: ["corp.example"]);

        var saved = await service.SetPolicyAsync("wg0", policy);

        Assert.True(saved.IsSuccess);
        var stored = service.GetPolicy("wg0");
        Assert.False(stored.AutoReconnect);
        Assert.Equal(NetworkManagerDnsMode.Split, stored.DnsMode);
        Assert.Equal(["corp.example"], stored.SearchDomains);
        Assert.Equal(("wg0", false, 0), Assert.Single(networkManager.AutoConnectChanges));
        var dns = Assert.Single(networkManager.DnsChanges);
        Assert.Equal(NetworkManagerDnsMode.Split, dns.Mode);
        Assert.Contains("corp.example", dns.Search);
    }

    [Fact]
    public async Task Stopping_an_inactive_tunnel_is_a_success()
    {
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var networkManager = new FakeNetworkManagerConnections();
        networkManager.Connections.Add(new NetworkManagerConnection("wg0", "wg0", false, ["user:alice"]));
        var service = Build(store, networkManager, owner: "alice");

        var stopped = await service.StopAsync("wg0");

        Assert.True(stopped.IsSuccess);
        Assert.Empty(networkManager.Deactivated);
        Assert.Equal(("wg0", false, 0), Assert.Single(networkManager.AutoConnectChanges));
    }

    [Fact]
    public async Task A_policy_change_on_an_active_connection_is_reapplied_live()
    {
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var networkManager = new FakeNetworkManagerConnections();
        networkManager.Connections.Add(new NetworkManagerConnection("wg0", "wg0-if", true, ["user:alice"]));
        var service = Build(store, networkManager, owner: "alice");

        var saved = await service.SetPolicyAsync(
            "wg0",
            new TunnelPolicy(AutoReconnect: true, DnsMode: NetworkManagerDnsMode.FullTunnel, DnsSearchDomains: []));

        Assert.True(saved.IsSuccess);
        Assert.Equal("wg0-if", Assert.Single(networkManager.Reapplied));
    }

    [Fact]
    public async Task A_policy_change_on_an_inactive_connection_is_not_reapplied()
    {
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var networkManager = new FakeNetworkManagerConnections();
        networkManager.Connections.Add(new NetworkManagerConnection("wg0", "wg0-if", false, ["user:alice"]));
        var service = Build(store, networkManager, owner: "alice");

        var saved = await service.SetPolicyAsync("wg0", new TunnelPolicy(DnsMode: NetworkManagerDnsMode.Split));

        Assert.True(saved.IsSuccess);
        Assert.Empty(networkManager.Reapplied);
    }

    [Fact]
    public async Task Deleting_a_profile_removes_the_networkmanager_connection_too()
    {
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var networkManager = new FakeNetworkManagerConnections();
        networkManager.Connections.Add(new NetworkManagerConnection("wg0", "wg0", false, ["user:alice"]));
        var service = Build(store, networkManager, owner: "alice");

        var result = await service.DeleteAsync("wg0");

        Assert.True(result.IsSuccess);
        Assert.Equal("wg0", Assert.Single(networkManager.Deleted));
    }

    [Fact]
    public async Task Deleting_a_profile_never_removes_a_foreign_networkmanager_connection()
    {
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var networkManager = new FakeNetworkManagerConnections();
        networkManager.Connections.Add(new NetworkManagerConnection("wg0", "wg0", false, ["user:someone-else"]));
        var service = Build(store, networkManager, owner: "alice");

        var result = await service.DeleteAsync("wg0");

        Assert.True(result.IsSuccess);
        Assert.Empty(networkManager.Deleted);
    }

    [Fact]
    public async Task The_presenter_notifies_the_desktop_when_connecting()
    {
        var view = new FakeTunnelView();
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var networkManager = new FakeNetworkManagerConnections();
        var service = new TunnelService(store, new FakeWireGuardRuntimeInspector(), networkManager: networkManager);
        var notifier = new FakeNotifier();
        var presenter = new TunnelListPresenter(view, service, notifier: notifier);

        view.RequestStart("wg0");
        await (presenter.PendingOperation ?? Task.CompletedTask);

        Assert.Null(view.Error);
        var notification = Assert.Single(notifier.Notifications);
        Assert.Contains("connected", notification.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_presenter_notifies_the_desktop_when_disconnecting()
    {
        var view = new FakeTunnelView();
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var runtime = new FakeWireGuardRuntimeInspector();
        runtime.InterfaceNames.Add("wg0");
        var networkManager = new FakeNetworkManagerConnections();
        var service = new TunnelService(store, runtime, networkManager: networkManager);
        var notifier = new FakeNotifier();
        var presenter = new TunnelListPresenter(view, service, notifier: notifier);

        view.RequestStop("wg0");
        await (presenter.PendingOperation ?? Task.CompletedTask);

        Assert.Null(view.Error);
        var notification = Assert.Single(notifier.Notifications);
        Assert.Contains("disconnected", notification.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_notify_send_notifier_labels_the_notification()
    {
        var runner = new FakeProcessRunner(_ => ProcessResult.Completed(0, string.Empty, string.Empty));
        var notifier = new NotifySendNotifier(runner);

        await notifier.NotifyAsync("VPN connected", "'wg0' is connected.");

        var request = Assert.Single(runner.Requests);
        Assert.Equal("notify-send", request.FileName);
        Assert.Contains("VPN Manager", request.Arguments);
        Assert.Contains("VPN connected", request.Arguments);
        Assert.Contains("'wg0' is connected.", request.Arguments);
    }

    [Fact]
    public async Task A_missing_notify_send_is_swallowed()
    {
        var notifier = new NotifySendNotifier(FakeProcessRunner.NotStarted());

        await notifier.NotifyAsync("VPN connected", "'wg0' is connected.");
    }

    [Fact]
    public async Task A_networkmanager_connection_resolves_to_the_interface_it_creates()
    {
        var networkManager = new FakeNetworkManagerConnections();
        networkManager.Connections.Add(new NetworkManagerConnection("work-vpn", "wg0", false, ["user:alice"]));
        var service = Build(new FakeWireGuardConfigStore(), networkManager, owner: "alice");

        await service.ListTunnelsAsync();

        Assert.Equal("wg0", service.ResolveInterfaceName("work-vpn"));
        Assert.Equal("wg0", service.ResolveInterfaceName("wg0"));
    }

    [Fact]
    public async Task Polling_the_transfer_computes_rates_from_consecutive_samples()
    {
        var view = new FakeTunnelView();
        var clock = new ManualTimeProvider();
        var reader = new FakeNetworkCounterReader();
        var service = new TunnelService(new FakeWireGuardConfigStore(), new FakeWireGuardRuntimeInspector());
        var presenter = new TunnelListPresenter(view, service, clock, counterReader: reader);

        reader.Counters["wg0"] = new NetworkCounters(1_000_000, 2_000_000);
        await presenter.SelectAsync("wg0");
        presenter.PollTransfer();

        Assert.NotNull(view.Transfer);
        Assert.Equal("0 B/s", view.Transfer!.DownloadRate);

        clock.Advance(TimeSpan.FromSeconds(2));
        reader.Counters["wg0"] = new NetworkCounters(3_000_000, 2_000_000);
        presenter.PollTransfer();

        Assert.NotEqual("0 B/s", view.Transfer!.DownloadRate);
        Assert.Equal("0 B/s", view.Transfer.UploadRate);
    }

    private static TunnelService Build(
        FakeWireGuardConfigStore store,
        FakeNetworkManagerConnections networkManager,
        string owner) =>
        new(
            store,
            new FakeWireGuardRuntimeInspector(),
            TunnelScope.User,
            new FakeApplicationStateStore(),
            networkManager,
            owner);
}
