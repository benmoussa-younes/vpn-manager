using Avalonia.Automation;
using Core.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
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
    public async Task AReadOnlySessionShowsAPersistentWarningBanner()
    {
        await HeadlessTestSession.RunAsync(() =>
        {
            var host = new FakeApplicationHost();
            var view = new TunnelListView();
            view.AttachHost(host);

            view.SetReadOnly(true);

            Assert.Contains(
                host.NotificationsService.Banners,
                banner => banner.Message.Contains("Read-only", StringComparison.Ordinal)
                    && banner.Severity == MessageSeverity.Warning);
        });
    }

    [Fact]
    public async Task TheEmptyStateOffersAnImportAction()
    {
        await HeadlessTestSession.RunAsync(async () =>
        {
            var host = new FakeApplicationHost();
            host.FilesService.Open = ["/tmp/a.conf"];
            var view = new TunnelListView();
            view.AttachHost(host);
            var imported = new List<string>();
            view.ImportRequested += path => imported.Add(path);

            view.ShowTunnels([], null);

            Assert.Equal("Import a profile\u2026", view.EmptyStateView.ActionLabel);
            view.EmptyStateView.ActionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Yield();
            Assert.Equal(["/tmp/a.conf"], imported);

            view.SetReadOnly(true);
            Assert.Equal(string.Empty, view.EmptyStateView.ActionLabel);
        });
    }

    [Fact]
    public async Task RowsCarryAnAutomationName()
    {
        await HeadlessTestSession.RunAsync(() =>
        {
            var host = new FakeApplicationHost();
            var view = new TunnelListView();
            view.AttachHost(host);

            view.ShowTunnels([Row("wg0", "Up")], "wg0");

            var row = Assert.IsAssignableFrom<Control>(Assert.Single(view.TunnelList.Items));
            Assert.Equal("wg0, Up", AutomationProperties.GetName(row));
        });
    }

    [Fact]
    public async Task AnErrorIsWrittenToTheApplicationLog()
    {
        await HeadlessTestSession.RunAsync(() =>
        {
            var host = new FakeApplicationHost();
            var logger = new FakeLogger();
            var view = new TunnelListView { Logger = logger };
            view.AttachHost(host);

            view.SetError("nmcli failed with exit code 4");

            Assert.Contains(
                logger.Entries,
                entry => entry.Level == LogLevel.Warning
                    && entry.Message.Contains("nmcli failed", StringComparison.Ordinal));
            Assert.Contains(host.NotificationsService.Toasts, toast => toast.Severity == MessageSeverity.Error);
        });
    }

    [Fact]
    public async Task RenamingAPageUpdatesItsHeaderWithoutReopening()
    {
        await HeadlessTestSession.RunAsync(() =>
        {
            var host = new FakeApplicationHost();
            var view = new TunnelListView();
            view.AttachHost(host);
            view.ShowTunnels([Row("wg0", "Up")], "wg0");
            view.OpenDetail("wg0");
            Assert.Equal("wg0", host.Snapshot!.Title);

            view.ShowTunnels([Row("wg0", "Up") with { DisplayName = "Home" }], "wg0");

            Assert.Equal("Home", host.Snapshot!.Title);
        });
    }

    [Fact]
    public async Task ClosingAsksToDisconnectWhenTheTunnelIsUp()
    {
        await HeadlessTestSession.RunAsync(async () =>
        {
            var host = new FakeApplicationHost();
            var view = new TunnelListView();
            view.AttachHost(host);

            host.DialogsService.Confirm = ConfirmResult.Accept;
            Assert.True(await view.ConfirmCloseAsync(["wg0"]));
            var request = Assert.Single(host.DialogsService.Requests);
            Assert.Contains("disconnect", request.Body, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(string.Empty, request.AlternativeLabel);

            host.DialogsService.Confirm = ConfirmResult.Alternative;
            Assert.False(await view.ConfirmCloseAsync(["wg0"]));
        });
    }

    [Fact]
    public async Task TheTrayMenuIsOnlyPublishedWhenAHostIsRegistered()
    {
        await HeadlessTestSession.RunAsync(() =>
        {
            var host = new FakeApplicationHost();
            var view = new TunnelListView();
            view.AttachHost(host);

            Assert.Null(host.TrayService.Menu);
            Assert.True(host.TrayService.HideCount >= 1);

            view.TrayAvailable = true;
            view.Publish();

            Assert.NotNull(host.TrayService.Menu);
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
