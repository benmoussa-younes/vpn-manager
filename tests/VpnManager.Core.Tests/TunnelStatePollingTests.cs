using VpnManager.Core.Model;
using VpnManager.Core.NetworkManager;
using VpnManager.Core.Presentation;
using VpnManager.Core.Services;
using VpnManager.Core.Tests.Fakes;

namespace VpnManager.Core.Tests;

/// <summary>
/// The quiet poll that keeps the window in step with a tunnel toggled outside the
/// application, through NetworkManager.
/// </summary>
public sealed class TunnelStatePollingTests
{
    [Fact]
    public async Task The_poll_shows_a_connection_activated_outside_the_application()
    {
        var view = new FakeTunnelView();
        var networkManager = new FakeNetworkManagerConnections();
        networkManager.Connections.Add(new NetworkManagerConnection("work-vpn", "wg0", false, ["user:alice"]));
        var presenter = await PresenterAsync(view, networkManager);

        Assert.Equal("Down", Assert.Single(view.Tunnels).State);
        var shown = view.ShowTunnelsCount;

        networkManager.Connections[0] = networkManager.Connections[0] with { IsActive = true };

        var changed = await presenter.PollStateAsync();

        Assert.True(changed);
        Assert.True(view.ShowTunnelsCount > shown);
        Assert.Equal("Up", Assert.Single(view.Tunnels).State);
    }

    [Fact]
    public async Task The_poll_leaves_the_view_alone_when_nothing_changed()
    {
        var view = new FakeTunnelView();
        var networkManager = new FakeNetworkManagerConnections();
        networkManager.Connections.Add(new NetworkManagerConnection("work-vpn", "wg0", false, ["user:alice"]));
        var presenter = await PresenterAsync(view, networkManager);
        var shown = view.ShowTunnelsCount;

        var changed = await presenter.PollStateAsync();

        Assert.False(changed);
        Assert.Equal(shown, view.ShowTunnelsCount);
    }

    [Fact]
    public async Task The_poll_stays_quiet_when_the_read_fails()
    {
        var store = new FakeWireGuardConfigStore("/config");
        store.Files["wg0"] = TestData.SampleConfigText;
        var runtime = new FakeWireGuardRuntimeInspector();
        var service = new TunnelService(store, runtime);
        var view = new FakeTunnelView();
        var presenter = new TunnelListPresenter(view, service);
        await presenter.RefreshAsync();
        var shown = view.ShowTunnelsCount;

        store.ListError = FakeWireGuardConfigStore.Denied("/config");
        runtime.NamesError = FakeWireGuardConfigStore.Denied("wg show interfaces");

        var changed = await presenter.PollStateAsync();

        Assert.False(changed);
        Assert.Equal(shown, view.ShowTunnelsCount);
        Assert.Null(view.Error);
    }

    private static async Task<TunnelListPresenter> PresenterAsync(FakeTunnelView view, FakeNetworkManagerConnections networkManager)
    {
        var store = new FakeWireGuardConfigStore("/config");
        var service = new TunnelService(
            store,
            new FakeWireGuardRuntimeInspector(),
            TunnelScope.User,
            networkManager: networkManager,
            networkManagerOwner: "alice");
        var presenter = new TunnelListPresenter(view, service);
        await presenter.RefreshAsync();
        return presenter;
    }
}
