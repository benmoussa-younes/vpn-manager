using Avalonia.Controls;
using Avalonia.Platform;
using Core.Diagnostics;
using Core.Mvp.Models;
using Core.Platform.Linux;
using Core.Results;
using Core.Mvp.Presentation;
using Core.Mvp.Services;
using Core.UI.Chrome;
using Core.UI.Dialogs;
using Core.UI.Hosting;
using Core.UI.Preferences;
using Avalonia.Threading;
using VpnManager.App.Tray;
using VpnManager.App.Views;
using VpnManager.Core.Presentation;

namespace VpnManager.App;

/// <summary>
/// The product shell over the shared <see cref="CoreWindow"/>: it owns the
/// presenter, the profile list, the primary menu, the tray commands and the
/// polling loops. The shared shell owns the chrome, dialogs, notifications and
/// the tray icon.
/// </summary>
internal sealed class ShellWindow : IAsyncDisposable
{
    private static readonly TimeSpan TransferPollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StatePollInterval = TimeSpan.FromSeconds(3);

    private readonly CoreHost _host;
    private readonly CompositionRoot _root;
    private readonly TunnelListView _content;
    private readonly TunnelListPresenter _presenter;
    private readonly AppearanceService _appearance;
    private readonly Func<CancellationToken, Task<bool>> _trayHostProbe;
    private readonly WindowPlacementService _placement;
    private readonly NmcliNetworkManagerMonitor _monitor = new();
    private readonly bool _readOnly;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _loopsStarted;
    private bool _disposed;

    internal ShellWindow(
        CoreHost host,
        CompositionRoot root,
        bool readOnly,
        Func<CancellationToken, Task<bool>>? trayHostProbe = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(root);

        _host = host;
        _root = root;
        _readOnly = readOnly;
        _trayHostProbe = trayHostProbe ?? StatusNotifierHostProbe.IsHostRegisteredAsync;

        var atomicFiles = new LinuxAtomicFileWriterFactory();
        var fileSystem = new LinuxFileSystemAdapter();
        _appearance = new AppearanceService(new JsonAppearanceStore(
            host.Paths.StateFile("preferences.json"),
            atomicFiles,
            fileSystem));

        Window = host.CreateWindow(LoadIcon("avares://vpn-manager/Assets/tray-icon.png"));
        Window.Title = TunnelListView.ProductTitle;
        Window.Icon = LoadIcon("avares://vpn-manager/Assets/app-icon.png");
        Window.Width = 1100;
        Window.Height = 720;
        Window.MinWidth = 720;
        Window.MinHeight = 520;
        // No tray host means no way back: the fallback is a real quit, with the
        // operator's confirmation when a tunnel is still connected.
        Window.CloseBehavior = CloseBehavior.Quit;

        var placementPath = host.Paths.StateFile("window.json");
        _placement = new WindowPlacementService(Window, placementPath, atomicFiles, fileSystem);
        if (!File.Exists(placementPath))
        {
            // First launch: let the shared placement code clamp the default size
            // to the work area instead of overflowing a small screen.
            Window.Opened += (_, _) =>
                Window.ApplyPlacement(new WindowPlacement(0, 0, 1100, 720, false));
        }

        _content = new TunnelListView();
        _content.AttachHost(Window);
        _content.SetReadOnly(readOnly);
        _content.WindowVisible = () => Window.IsVisible;

        _presenter = new TunnelListPresenter(
            _content,
            root.Service,
            counterReader: root.CounterReader,
            physicalLink: root.PhysicalLink,
            routingPrerequisite: root.RoutingPrerequisite,
            notifier: root.Notifier,
            journal: root.Journal);

        Window.ConfirmQuit = () => new ValueTask<bool>(_presenter.ConfirmQuitAsync());
        Window.ConfirmClose = () => new ValueTask<bool>(_presenter.ConfirmQuitAsync());

        _content.StateBackupRequested += path => Report(root.StateStore.Export(path));
        _content.StateRestoreRequested += RestoreState;

        Window.SetApplicationMenu(BuildApplicationMenu(readOnly));
        Window.SetAbout(BuildAbout());
        Window.SetAppearance(_appearance);
        Window.CommandInvoked += (_, id) => _content.HandleCommand(id);
        _host.TrayCommandInvoked += id => _content.HandleCommand(id);

        Window.Navigation.Push(TunnelListView.ProductTitle, string.Empty, _content);
        _content.Publish();

        Window.Opened += OnOpened;
        _ = ApplyTrayAvailabilityAsync();
    }

    /// <summary>Gets the shell window the application lifetime hosts.</summary>
    internal CoreWindow Window { get; }

    /// <summary>Gets or sets the delay the polling loops await; a test completes it at once.</summary>
    internal Func<TimeSpan, CancellationToken, Task> DelayAsync { get; set; } =
        static (delay, token) => Task.Delay(delay, token);

    /// <summary>Applies the stored appearance preferences after the base theme is set.</summary>
    internal void ApplyAppearance()
    {
        try
        {
            _appearance.Apply();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _root.Logger.Log(LogLevel.Warning, "The appearance preference could not be applied.");
        }
    }

    /// <summary>Stops the polling loops and disposes the presenter.</summary>
    /// <returns>A task that completes when the presenter has released its resources.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        _monitor.Dispose();
        await _presenter.DisposeAsync().ConfigureAwait(true);
        _lifetime.Dispose();
    }

    /// <summary>
    /// Flips the close behaviour to hide only when a tray host is registered;
    /// otherwise the window keeps the quit fallback and no tray menu is published.
    /// </summary>
    private async Task ApplyTrayAvailabilityAsync()
    {
        bool available;
        try
        {
            available = await _trayHostProbe(_lifetime.Token).ConfigureAwait(true);
        }
        catch (Exception)
        {
            available = false;
        }

        if (!available || _disposed)
        {
            return;
        }

        Window.CloseBehavior = CloseBehavior.HideToTray;
        _content.TrayAvailable = true;
        _content.Publish();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            await _presenter.InitializeAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _content.SetError(exception.Message);
        }

        if (_loopsStarted || _disposed)
        {
            return;
        }

        _loopsStarted = true;
        _monitor.Changed += OnNetworkManagerChanged;
        _monitor.Start();
        _ = PollTransferLoopAsync();
        _ = PollStateLoopAsync();
    }

    private async Task PollTransferLoopAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            try
            {
                await DelayAsync(TransferPollInterval, _lifetime.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (Window.IsVisible)
            {
                try
                {
                    _presenter.PollTransfer();
                }
                catch (Exception)
                {
                    // A poll is best effort; the next one recovers.
                }
            }
        }
    }

    private void OnNetworkManagerChanged() =>
        Dispatcher.UIThread.Post(() => _ = PollStateFromEventAsync());

    private async Task PollStateFromEventAsync()
    {
        try
        {
            await _presenter.PollStateAsync().ConfigureAwait(true);
        }
        catch (Exception)
        {
            // A monitor hint is best effort; the periodic poll recovers.
        }
    }

    private async Task PollStateLoopAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            try
            {
                await DelayAsync(StatePollInterval, _lifetime.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                // NetworkManager owns autoconnect; the old application retry would
                // race it and is deliberately not called here.
                await _presenter.PollStateAsync().ConfigureAwait(true);
            }
            catch (Exception)
            {
                // The next poll recovers.
            }
        }
    }

    private void Report(Result<string> result)
    {
        if (result.IsSuccess)
        {
            _content.ShowToast($"State written to {result.Value}.");
            return;
        }

        _content.SetError(result.Error.Message);
    }

    private void RestoreState(string path)
    {
        if (_readOnly)
        {
            _content.SetError("Read-only session: restoring the state is disabled.");
            return;
        }

        var loaded = _root.StateStore.LoadFrom(path);
        if (!loaded.TryGetValue(out var state))
        {
            _content.SetError(loaded.Error.Message);
            return;
        }

        var saved = _root.StateStore.Save(state);
        if (saved.IsSuccess)
        {
            _content.ShowToast("State restored.");
            return;
        }

        _content.SetError(saved.Error.Message);
    }

    private static CommandSet BuildApplicationMenu(bool readOnly) => new(
    [
        new AppCommand("new", "New tunnel\u2026", Shortcut: "Ctrl+N", IsEnabled: !readOnly, Group: "tunnel"),
        new AppCommand("edit", "Edit tunnel\u2026", Shortcut: "Ctrl+E", IsEnabled: !readOnly, Group: "tunnel"),
        new AppCommand("rename", "Rename tunnel", IsEnabled: !readOnly, Group: "tunnel"),
        new AppCommand("delete", "Delete tunnel", Shortcut: "Delete", IsEnabled: !readOnly, Group: "tunnel"),
        new AppCommand("refresh", "Refresh", Shortcut: "Ctrl+R", Group: "view"),
        new AppCommand("paste", "Paste profile\u2026", IsEnabled: !readOnly, Group: "import"),
        new AppCommand("import", "Import\u2026", IsEnabled: !readOnly, Group: "import"),
        new AppCommand("export", "Export\u2026", Group: "import"),
        new AppCommand("backup", "Back up state\u2026", Group: "state"),
        new AppCommand("restore", "Restore state\u2026", IsEnabled: !readOnly, Group: "state"),
    ]);

    private static AboutInfo BuildAbout() => new(
        TunnelListView.ProductTitle,
        Program.Version,
        string.Concat(
            "Copyright (C) 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>\n\n",
            "This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.\n\n",
            "This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE."),
        new Uri("https://github.com/benmoussa-younes/vpn-manager"),
        "GNU GPL v3.0 or later",
        new Uri("https://www.gnu.org/licenses/gpl-3.0.html"));

    private static WindowIcon? LoadIcon(string uri)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri(uri));
            return new WindowIcon(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
