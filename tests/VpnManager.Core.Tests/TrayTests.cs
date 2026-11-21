using VpnManager.Core.Presentation;
using VpnManager.Core.Services;
using VpnManager.Core.Tests.Fakes;

namespace VpnManager.Core.Tests;

/// <summary>The system-tray menu model and the quit flow.</summary>
public sealed class TrayTests
{
    private static TunnelListItem Row(string name, string state) => new(name, state, "no peers", "configured", null);

    [Fact]
    public void The_menu_toggles_the_window_and_quits()
    {
        Assert.Equal("Show window", TrayMenuBuilder.Build([], windowVisible: false)[0].Header);
        Assert.Equal(TrayMenuAction.ToggleWindow, TrayMenuBuilder.Build([], windowVisible: false)[0].Action);
        Assert.Equal("Hide window", TrayMenuBuilder.Build([], windowVisible: true)[0].Header);
        Assert.Equal(TrayMenuAction.Quit, TrayMenuBuilder.Build([], windowVisible: true)[^1].Action);
    }

    [Fact]
    public void At_most_three_tunnels_are_shown_with_their_verb()
    {
        var items = TrayMenuBuilder.Build(
            [Row("a", "Down"), Row("b", "Up"), Row("c", "Down"), Row("d", "Down")],
            windowVisible: true);

        var tunnels = items
            .Where(item => item.Action is TrayMenuAction.Connect or TrayMenuAction.Disconnect)
            .ToArray();

        Assert.Equal(TrayMenuBuilder.MaximumTunnels, tunnels.Length);
        Assert.Contains(tunnels, item => item.TunnelName == "b" && item.Action == TrayMenuAction.Disconnect);
        Assert.DoesNotContain(tunnels, item => item.TunnelName == "d");
    }

    [Fact]
    public void The_connected_tunnels_come_first()
    {
        var items = TrayMenuBuilder.Build([Row("a", "Down"), Row("b", "Up")], windowVisible: true);

        Assert.Equal("b", items.First(item => item.TunnelName is not null).TunnelName);
    }

    [Fact]
    public void A_tunnel_is_listed_under_its_display_name()
    {
        var row = new TunnelListItem("wg0", "Up", "no peers", "configured", null, DisplayName: "Work VPN");

        var items = TrayMenuBuilder.Build([row], windowVisible: true);

        Assert.Contains(items, item => item.Header == "Disconnect Work VPN" && item.TunnelName == "wg0");
    }

    [Fact]
    public async Task Quitting_with_a_connected_tunnel_asks_and_can_cancel()
    {
        var view = new FakeTunnelView { QuitAnswer = QuitChoice.Cancel };
        var presenter = await PresenterWithConnectedTunnelAsync(view);

        var quit = await presenter.ConfirmQuitAsync();

        Assert.False(quit);
        Assert.Equal(["wg0"], Assert.Single(view.QuitConfirmations));
        Assert.True(view.WindowShown);
    }

    [Fact]
    public async Task Quitting_disconnects_the_connected_tunnels_when_asked()
    {
        var view = new FakeTunnelView { QuitAnswer = QuitChoice.DisconnectAndQuit };
        var presenter = await PresenterWithConnectedTunnelAsync(view);

        var quit = await presenter.ConfirmQuitAsync();

        Assert.True(quit);
        Assert.True(view.WindowShown);
    }

    [Fact]
    public async Task Quitting_with_no_connected_tunnel_exits_without_asking()
    {
        var view = new FakeTunnelView();
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var service = new TunnelService(store, new FakeWireGuardRuntimeInspector());
        var presenter = new TunnelListPresenter(view, service);

        var quit = await presenter.ConfirmQuitAsync();

        Assert.True(quit);
        Assert.Empty(view.QuitConfirmations);
        Assert.False(view.WindowShown);
    }

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
