using Core.Mvp.Services;
using VpnManager.App.Tests.Fakes;
using VpnManager.App.Views;
using VpnManager.Core.Presentation;
using ProductQuitChoice = VpnManager.Core.Presentation.QuitChoice;

namespace VpnManager.App.Tests;

public sealed class TunnelListViewTests
{
    [Fact]
    public async Task TheListRendersRowsAndPublishesTheNewAndEditCommands()
    {
        await HeadlessTestSession.RunAsync(() =>
        {
            var host = new FakeApplicationHost();
            var view = new TunnelListView();
            view.AttachHost(host);

            view.ShowTunnels([Row("wg0", "Up")], "wg0");

            Assert.Single(view.TunnelList.Items);
            Assert.NotNull(host.Snapshot);
            Assert.Contains(host.Snapshot!.Commands.Commands, command => command.Id == "new");
            Assert.Contains(host.Snapshot.Commands.Commands, command => command.Id == "edit");
            Assert.Contains(host.Snapshot.Commands.Commands, command => command.Id == "toggle");
        });
    }

    [Fact]
    public async Task AReadOnlySessionRefusesTheMutatingCommands()
    {
        await HeadlessTestSession.RunAsync(() =>
        {
            var host = new FakeApplicationHost();
            var view = new TunnelListView();
            view.AttachHost(host);
            view.SetReadOnly(true);

            view.HandleCommand("new");
            view.HandleCommand("delete");
            view.HandleCommand("restore");

            Assert.Equal(3, host.NotificationsService.Toasts.Count);
            Assert.All(host.NotificationsService.Toasts, toast => Assert.Contains("Read-only", toast.Message, StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task RefreshAndDropRaiseTheSemanticEvents()
    {
        await HeadlessTestSession.RunAsync(() =>
        {
            var host = new FakeApplicationHost();
            var view = new TunnelListView();
            view.AttachHost(host);
            var refreshed = 0;
            var imported = new List<string>();
            view.RefreshRequested += () => refreshed++;
            view.ImportRequested += path => imported.Add(path);

            view.HandleCommand("refresh");
            view.HandleDrop(["/tmp/a.conf", "/tmp/not-a-profile.txt"]);

            Assert.Equal(1, refreshed);
            Assert.Equal(["/tmp/a.conf"], imported);
        });
    }

    [Fact]
    public async Task OpeningADetailPagePushesItOnTheNavigationStack()
    {
        await HeadlessTestSession.RunAsync(() =>
        {
            var host = new FakeApplicationHost();
            var view = new TunnelListView();
            view.AttachHost(host);
            view.ShowTunnels([Row("wg0", "Up")], "wg0");

            view.OpenDetail("wg0");

            var pushed = Assert.Single(host.NavigationService.Pushed);
            Assert.Equal("wg0", pushed.Title);
            Assert.IsType<TunnelDetailView>(pushed.View);
        });
    }

    [Fact]
    public async Task TheQuitConfirmationMapsTheDialogAnswer()
    {
        await HeadlessTestSession.RunAsync(async () =>
        {
            var host = new FakeApplicationHost();
            var view = new TunnelListView();
            view.AttachHost(host);

            host.DialogsService.Confirm = ConfirmResult.Accept;
            var choice = await view.ConfirmQuitAsync(["wg0"]);

            Assert.Equal(ProductQuitChoice.DisconnectAndQuit, choice);
            Assert.Single(host.DialogsService.Requests);
        });
    }

    [Fact]
    public void TheTrayCommandsKeepTheWindowAndTunnelVerbs()
    {
        var items = new[]
        {
            new TrayMenuItem("Show window", TrayMenuAction.ToggleWindow),
            TrayMenuItem.Separator,
            new TrayMenuItem("Connect wg0", TrayMenuAction.Connect, "wg0"),
            TrayMenuItem.Separator,
            new TrayMenuItem("Quit", TrayMenuAction.Quit),
        };

        var commands = TunnelListView.BuildTrayCommands(items);

        Assert.Contains(commands.Commands, command => command.Id == "tray.show");
        Assert.Contains(commands.Commands, command => command.Id == "connect:wg0");
        Assert.Contains(commands.Commands, command => command.Id == "tray.quit");
    }

    private static TunnelListItem Row(string name, string state) =>
        new(name, state, "1 peer", "configured", null);
}
