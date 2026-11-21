using VpnManager.Core.Presentation;
using VpnManager.Core.Services;
using VpnManager.Core.Tests.Fakes;

namespace VpnManager.Core.Tests;

/// <summary>The quit and close confirmations: what is asked, and when the VPN is disconnected.</summary>
public sealed class QuitFlowTests
{
    [Fact]
    public async Task Closing_with_a_connected_tunnel_asks_and_can_cancel()
    {
        var view = new FakeTunnelView { CloseAnswer = false };
        var presenter = await PresenterWithConnectedTunnelAsync(view);

        var close = await presenter.ConfirmCloseAsync();

        Assert.False(close);
        Assert.Equal(["wg0"], Assert.Single(view.CloseConfirmations));
        Assert.True(view.WindowShown);
    }

    [Fact]
    public async Task Closing_disconnects_the_connected_tunnels_when_asked()
    {
        var view = new FakeTunnelView { CloseAnswer = true };
        var presenter = await PresenterWithConnectedTunnelAsync(view);

        var close = await presenter.ConfirmCloseAsync();

        Assert.True(close);
        Assert.Single(view.CloseConfirmations);
    }

    [Fact]
    public async Task Closing_with_no_connected_tunnel_exits_without_asking()
    {
        var view = new FakeTunnelView();
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var service = new TunnelService(store, new FakeWireGuardRuntimeInspector());
        var presenter = new TunnelListPresenter(view, service);

        var close = await presenter.ConfirmCloseAsync();

        Assert.True(close);
        Assert.Empty(view.CloseConfirmations);
        Assert.False(view.WindowShown);
    }

    private static async Task<TunnelListPresenter> PresenterWithConnectedTunnelAsync(FakeTunnelView view)
    {
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var runtime = new FakeWireGuardRuntimeInspector();
        runtime.InterfaceNames.Add("wg0");
        var service = new TunnelService(store, runtime);
        var presenter = new TunnelListPresenter(view, service);
        await presenter.RefreshAsync();
        return presenter;
    }
}
