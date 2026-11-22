using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Core.Diagnostics;
using Core.Mvp.Services;
using VpnManager.App.Tests.Fakes;
using VpnManager.App.Views;
using VpnManager.Core.Model;
using VpnManager.Core.NetworkManager;
using VpnManager.Core.Presentation;

namespace VpnManager.App.Tests;

public sealed class TunnelListViewTests
{
    [Fact]
    public async Task TheSidebarListsRowsAndTheToolbarIsGone()
    {
        await HeadlessTestSession.RunAsync(() =>
        {
            var host = new FakeApplicationHost();
            var view = new TunnelListView();
            view.AttachHost(host);

            view.ShowTunnels([Row("wg0", "Up")], "wg0");

            Assert.Single(view.TunnelList.Items);
            Assert.NotNull(host.Snapshot);
            Assert.Empty(host.Snapshot!.Commands.Commands);
        });
    }

    [Fact]
    public async Task SelectingAProfileRaisesTheSelectionAndFillsThePaneHeader()
    {
        await HeadlessTestSession.RunAsync(() =>
        {
            var host = new FakeApplicationHost();
            var view = new TunnelListView();
            view.AttachHost(host);
            var selected = new List<string>();
            view.TunnelSelected += name => selected.Add(name);
            view.ShowTunnels([Row("wg0", "Up"), Row("wg1", "Down")], null);

            view.TunnelList.SelectedIndex = 0;

            Assert.Equal(["wg0"], selected);
            Assert.Equal("wg0", view.PaneNameText.Text);
            Assert.Equal("Disconnect", view.ConnectButton.Content);
        });
    }

    [Fact]
    public async Task TheEditorIsEmbeddedInThePaneInsteadOfANewPage()
    {
        await HeadlessTestSession.RunAsync(() =>
        {
            var host = new FakeApplicationHost();
            var view = new TunnelListView();
            view.AttachHost(host);

            view.ShowEditor(Snapshot());

            Assert.NotNull(view.OpenEditor);
            Assert.True(view.OpenEditor!.IsVisible);
            Assert.Empty(host.NavigationService.Pushed);
        });
    }

    [Fact]
    public async Task ThePolicyCardIsEmbeddedAndAppliesOnChange()
    {
        await HeadlessTestSession.RunAsync(() =>
        {
            var host = new FakeApplicationHost();
            var view = new TunnelListView();
            view.AttachHost(host);
            var applied = new List<TunnelPolicyInput>();
            view.PolicySubmitted += input => applied.Add(input);
            view.ShowTunnels([Row("wg0", "Up")], "wg0");

            view.ShowPolicy(new TunnelPolicySnapshot("wg0", AutoReconnect: true, NetworkManagerDnsMode.Profile, []));
            view.PolicyView.ReconnectBox.IsChecked = false;

            Assert.True(view.PolicyView.IsVisible);
            Assert.Empty(host.NavigationService.Pushed);
            Assert.Contains(applied, input => !input.AutoReconnect);
        });
    }

    [Fact]
    public async Task TheDisplayNameCommitsInline()
    {
        await HeadlessTestSession.RunAsync(() =>
        {
            var host = new FakeApplicationHost();
            var view = new TunnelListView();
            view.AttachHost(host);
            var renamed = new List<TunnelRenameInput>();
            view.RenameSubmitted += input => renamed.Add(input);
            view.ShowTunnels([Row("wg0", "Up")], "wg0");
            view.ShowEditor(Snapshot());

            view.ShowRename(new TunnelRenameSnapshot("wg0", "wg0"));
            view.DisplayNameBox.Text = "Home";
            view.DisplayNameBox.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Enter,
            });

            Assert.Equal("Home", Assert.Single(renamed).DisplayName);
            Assert.Empty(host.NavigationService.Pushed);
        });
    }

    [Fact]
    public async Task AnUnsavedFormAsksBeforeTheSelectionMoves()
    {
        await HeadlessTestSession.RunAsync(() =>
        {
            var host = new FakeApplicationHost();
            var view = new TunnelListView();
            view.AttachHost(host);
            view.ShowTunnels([Row("wg0", "Up"), Row("wg1", "Down")], "wg0");
            view.ShowEditor(Snapshot());
            view.OpenEditor!.NameBox.Text = "wg0-edited";
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            view.TunnelList.SelectedIndex = 1;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, host.DialogsService.SavePrompts);
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
            Assert.Contains(host.NotificationsService.Banners, banner => banner.Message.Contains("Read-only", StringComparison.Ordinal));
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

    private static EditorSnapshot Snapshot() => new(
        "Edit tunnel",
        IsNew: false,
        Name: "wg0",
        NameEditable: false,
        Addresses: "10.2.0.2/32",
        Dns: "10.2.0.1",
        ListenPort: string.Empty,
        Mtu: string.Empty,
        Table: string.Empty,
        RoutingMode: TunnelRoutingMode.Full,
        HasPrivateKey: true,
        PublicKey: "derived",
        Peers: []);

    private static TunnelListItem Row(string name, string state) =>
        new(name, state, "1 peer", "configured", null);
}
