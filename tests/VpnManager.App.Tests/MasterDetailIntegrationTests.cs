using Avalonia.Input;
using Avalonia.Threading;
using Core.Mvp.Services;
using VpnManager.App.Tests.Fakes;
using VpnManager.App.Views;
using VpnManager.Core.Model;
using VpnManager.Core.NetworkManager;
using VpnManager.Core.Presentation;
using VpnManager.Core.Services;

namespace VpnManager.App.Tests;

/// <summary>
/// The pane driven by the real presenter and service: selection, save, cancel,
/// policy on change, foreign connections and the read-only surface.
/// </summary>
public sealed class MasterDetailIntegrationTests
{
    [Fact]
    public async Task SelectingAProfileLoadsTheWholePane()
    {
        await HeadlessTestSession.RunAsync(async () =>
        {
            var (view, presenter, _, _, _) = Build();

            await presenter.RefreshAsync();
            Select(view, 0);
            await presenter.PendingOperation!;

            Assert.True(view.OpenEditor!.IsVisible);
            Assert.True(view.PolicyView.IsVisible);
            Assert.True(view.DisplayCardVisible);
            Assert.Equal("wg0", view.DisplayNameBox.Text);
            Assert.Equal("wg0", view.PaneNameText.Text);
        });
    }

    [Fact]
    public async Task CancellingAnEditReloadsTheStoredConfiguration()
    {
        await HeadlessTestSession.RunAsync(async () =>
        {
            var (view, presenter, _, _, _) = Build();
            await presenter.RefreshAsync();
            Select(view, 0);
            await presenter.PendingOperation!;

            view.OpenEditor!.NameBox.Text = "wg0-edited";
            Dispatcher.UIThread.RunJobs();
            presenter.CancelEditor();
            await presenter.SelectAsync("wg0");

            Assert.Equal("wg0", view.OpenEditor!.NameBox.Text);
            Assert.True(view.PolicyView.IsVisible);
        });
    }

    [Fact]
    public async Task SavingWritesTheFileAndReloadsThePane()
    {
        await HeadlessTestSession.RunAsync(async () =>
        {
            var (view, presenter, store, _, _) = Build();
            await presenter.RefreshAsync();
            Select(view, 0);
            await presenter.PendingOperation!;

            view.OpenEditor!.AddressesBox.Text = "10.10.0.12/32";
            Dispatcher.UIThread.RunJobs();
            view.OpenEditor!.HandleCommand("editor.save");
            await presenter.PendingOperation!;

            Assert.Contains(store.Writes, write => write.Content.Contains("10.10.0.12/32", StringComparison.Ordinal));
            Assert.True(view.OpenEditor!.IsVisible);
        });
    }

    [Fact]
    public async Task AProfileChangeAppliesThePolicyOnChange()
    {
        await HeadlessTestSession.RunAsync(async () =>
        {
            var (view, presenter, _, service, _) = Build();
            await presenter.RefreshAsync();
            Select(view, 0);
            await presenter.PendingOperation!;

            view.PolicyView.SearchBox.Text = "corp.example";
            view.PolicyView.SearchBox.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Enter,
            });
            await presenter.PendingOperation!;

            Assert.Equal(["corp.example"], service.GetPolicy("wg0").SearchDomains);
        });
    }

    [Fact]
    public async Task SavingTheDirtyPromptKeepsTheEditedProfile()
    {
        await HeadlessTestSession.RunAsync(async () =>
        {
            var (view, presenter, store, _, host) = Build();
            host.DialogsService.SavePromptAnswer = SavePromptChoice.Save;
            await presenter.RefreshAsync();
            Select(view, 0);
            await presenter.PendingOperation!;
            view.OpenEditor!.AddressesBox.Text = "10.10.0.20/32";
            Dispatcher.UIThread.RunJobs();
            view.ShowTunnels([Row("wg0", "Up"), Row("wg1", "Down")], "wg0");

            view.TunnelList.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            await (presenter.PendingOperation ?? Task.CompletedTask);

            Assert.Contains(store.Writes, write => write.Content.Contains("10.10.0.20/32", StringComparison.Ordinal));
            Assert.Equal(0, view.TunnelList.SelectedIndex);
        });
    }

    [Fact]
    public async Task TheDnsModeSelectorAppliesOnChange()
    {
        await HeadlessTestSession.RunAsync(async () =>
        {
            var (view, presenter, _, service, _) = Build();
            await presenter.RefreshAsync();
            Select(view, 0);
            await presenter.PendingOperation!;

            view.PolicyView.ModeBox.SelectedIndex = (int)NetworkManagerDnsMode.Split;
            await (presenter.PendingOperation ?? Task.CompletedTask);

            Assert.Equal(NetworkManagerDnsMode.Split, service.GetPolicy("wg0").DnsMode);
        });
    }

    [Fact]
    public async Task AForeignConnectionShowsTheHeaderButNoEditor()
    {
        await HeadlessTestSession.RunAsync(async () =>
        {
            var (view, presenter, _, _, _) = Build(withFile: false, foreign: true);
            await presenter.RefreshAsync();
            Select(view, 0);
            await presenter.PendingOperation!;

            Assert.False(view.OpenEditor!.IsVisible);
            Assert.False(view.PolicyView.IsVisible);
            Assert.False(view.DisplayCardVisible);
            Assert.True(view.ConnectButton.IsEnabled);
        });
    }

    [Fact]
    public async Task AReadOnlySessionDisablesTheEditableSurface()
    {
        await HeadlessTestSession.RunAsync(async () =>
        {
            var (view, presenter, _, _, _) = Build();
            await presenter.RefreshAsync();
            Select(view, 0);
            await presenter.PendingOperation!;

            view.SetReadOnly(true);

            Assert.False(view.OpenEditor!.IsEnabled);
            Assert.False(view.PolicyView.IsEnabled);
            Assert.False(view.DisplayNameBox.IsEnabled);
            Assert.False(view.ConnectButton.IsEnabled);
        });
    }

    [Fact]
    public async Task ANewProfileHidesThePolicyAndDisplayNameCards()
    {
        await HeadlessTestSession.RunAsync(async () =>
        {
            var (view, presenter, _, _, _) = Build();
            await presenter.RefreshAsync();

            presenter.BeginCreate();

            Assert.True(view.OpenEditor!.IsVisible);
            Assert.False(view.PolicyView.IsVisible);
            Assert.False(view.DisplayCardVisible);
        });
    }

    [Fact]
    public async Task CancellingTheDirtyPromptRestoresThePreviousSelection()
    {
        await HeadlessTestSession.RunAsync(async () =>
        {
            var (view, presenter, _, _, host) = Build();
            host.DialogsService.SavePromptAnswer = SavePromptChoice.Cancel;
            await presenter.RefreshAsync();
            Select(view, 0);
            await presenter.PendingOperation!;
            view.OpenEditor!.NameBox.Text = "edited";
            Dispatcher.UIThread.RunJobs();
            view.ShowTunnels([Row("wg0", "Up"), Row("wg1", "Down")], "wg0");

            view.TunnelList.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(0, view.TunnelList.SelectedIndex);
        });
    }

    private static void Select(TunnelListView view, int index)
    {
        view.TunnelList.SelectedIndex = index;
        Dispatcher.UIThread.RunJobs();
    }

    private static (TunnelListView View, TunnelListPresenter Presenter, FakeConfigStore Store, TunnelService Service, FakeApplicationHost Host) Build(
        bool withFile = true,
        bool foreign = false)
    {
        var store = new FakeConfigStore();
        if (withFile)
        {
            store.Files["wg0"] = AppTestData.SampleConfig;
        }

        var networkManager = new FakeNetworkManager();
        if (foreign)
        {
            networkManager.Connections.Add(new NetworkManagerConnection("Corp", "wg0", false, ["user:alice"]));
        }

        var service = new TunnelService(
            store,
            new FakeRuntimeInspector(),
            TunnelScope.User,
            new FakeStateStore(),
            networkManager,
            "alice",
            importDirectory: Path.Combine(Path.GetTempPath(), "vpn-manager-pane-tests"));
        var view = new TunnelListView();
        var host = new FakeApplicationHost();
        view.AttachHost(host);
        var presenter = new TunnelListPresenter(view, service);
        return (view, presenter, store, service, host);
    }

    private static TunnelListItem Row(string name, string state) =>
        new(name, state, "1 peer", "configured", null);
}
