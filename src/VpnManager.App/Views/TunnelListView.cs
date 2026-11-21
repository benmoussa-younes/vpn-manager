using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Core.Mvp.Composition;
using Core.Mvp.Presentation;
using Core.Mvp.Services;
using Core.Mvp.Views;
using Core.Results;
using VpnManager.App.Controls;
using VpnManager.Core.Presentation;
using QuitChoice = VpnManager.Core.Presentation.QuitChoice;

namespace VpnManager.App.Views;

/// <summary>
/// The master list of tunnels (MVP, passive view): one row per tunnel, each with
/// its live transfer, and the empty state. Activating a row pushes the tunnel's
/// <see cref="TunnelDetailView"/> page; the editor is pushed from there. The view
/// holds no rules about tunnels, keys or files.
/// </summary>
public sealed class TunnelListView : UserControl, ITunnelListView, IBusyView
{
    private const string ReadOnlyMessage =
        "Read-only session: creating, editing, deleting and starting or stopping tunnels is disabled.";

    private const string EmptyMessage = "The tunnels list is empty.";

    /// <summary>The product name shown in the header bar and the window title.</summary>
    internal const string ProductTitle = "VPN Manager";

    private static readonly TransferView ZeroTransfer = new("0 B/s", "0 B", "0 B/s", "0 B");

    private readonly ListBox _tunnelList = new() { SelectionMode = SelectionMode.Single, Background = Brushes.Transparent };
    private readonly TextBlock _emptyText;
    private readonly List<string> _rowNames = [];
    private readonly Dictionary<string, string> _tunnelStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (TextBlock Down, TextBlock Up)> _rowTransfers = new(StringComparer.Ordinal);
    private IApplicationHost? _host;
    private IFilePicker? _picker;
    private TunnelEditorView? _editor;
    private TunnelDetailView? _detail;
    private TunnelRenameView? _rename;
    private IReadOnlyList<TunnelListItem> _lastTunnels = [];
    private IReadOnlyDictionary<string, TransferView> _transfers = new Dictionary<string, TransferView>(StringComparer.Ordinal);
    private string _status = string.Empty;
    private bool _suspendSelection;
    private bool _readOnly;
    private bool _busy;

    /// <summary>Initializes the master list and its empty message.</summary>
    public TunnelListView()
    {
        _tunnelList.SelectionChanged += OnTunnelSelectionChanged;
        _tunnelList.DoubleTapped += OnTunnelDoubleTapped;
        _tunnelList.KeyDown += OnTunnelKeyDown;

        // A `.conf` dropped anywhere on the page is imported.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        _emptyText = new TextBlock
        {
            Text = EmptyMessage,
            Classes = { "muted" },
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(24),
            IsVisible = false,
        };

        var listPanel = new Panel();
        listPanel.Children.Add(_tunnelList);
        listPanel.Children.Add(_emptyText);

        Content = listPanel;
    }

    /// <inheritdoc />
    public event Action<bool>? BusyChanged;

    /// <inheritdoc />
    public event Action? RefreshRequested;

    /// <inheritdoc />
    public event Action<string>? TunnelSelected;

    /// <inheritdoc />
    public event Action? CreateRequested;

    /// <inheritdoc />
    public event Action<string>? EditRequested;

    /// <inheritdoc />
    public event Action<string>? DeleteRequested;

    /// <inheritdoc />
    public event Action<string>? RenameRequested;

    /// <inheritdoc />
    public event Action<TunnelRenameInput>? RenameSubmitted;

    /// <inheritdoc />
    public event Action? RenameCancelRequested;

    /// <inheritdoc />
    public event Action<string>? StartRequested;

    /// <inheritdoc />
    public event Action<string>? StopRequested;

    /// <inheritdoc />
    public event Action<string>? ImportRequested;

    /// <inheritdoc />
    public event Action<string>? ProfileTextPasted;

    /// <inheritdoc />
    public event Action<string>? ExportRequested;

    /// <inheritdoc />
    public event Action<EditorInput>? SaveRequested;

    /// <inheritdoc />
    public event Action? EditorCancelRequested;

    /// <inheritdoc />
    public event Action? EditorPeerAddRequested;

    /// <inheritdoc />
    public event Action<int>? EditorPeerRemoveRequested;

    /// <inheritdoc />
    public event Action? EditorKeyPairRequested;

    /// <summary>The operator chose a destination to back the application state up to.</summary>
    public event Action<string>? StateBackupRequested;

    /// <summary>The operator chose a state document to restore from.</summary>
    public event Action<string>? StateRestoreRequested;

    /// <summary>Gets the tunnel list control, for tests and layout checks.</summary>
    public ListBox TunnelList => _tunnelList;

    /// <summary>Gets the centred empty message, for tests.</summary>
    internal TextBlock EmptyText => _emptyText;

    /// <summary>Gets the editor currently open on the shell stack, or <see langword="null"/>.</summary>
    internal TunnelEditorView? OpenEditor => _editor;

    /// <summary>Gets the detail page currently open on the shell stack, or <see langword="null"/>.</summary>
    internal TunnelDetailView? OpenDetailPage => _detail;

    /// <summary>Gets the rename page currently open on the shell stack, or <see langword="null"/>.</summary>
    internal TunnelRenameView? OpenRenamePage => _rename;

    /// <summary>Gets the status line published as the shell subtitle.</summary>
    internal string Status => _status;

    /// <summary>Gets a row's rendered transfer label ("down|up"), for tests.</summary>
    /// <param name="name">The tunnel name.</param>
    /// <returns>The two labels, or <see langword="null"/> when the row is absent.</returns>
    internal string? RowTransferText(string name) =>
        _rowTransfers.TryGetValue(name, out var transfer)
            ? string.Concat(transfer.Down.Text, "|", transfer.Up.Text)
            : null;

    /// <summary>Gets or sets the file and clipboard seam, for tests.</summary>
    internal IFilePicker Picker
    {
        get => _picker ??= _host?.Files ?? throw new InvalidOperationException("The view is not attached to a host with a file picker.");
        set => _picker = value;
    }

    /// <summary>Attaches the shell host the content drives.</summary>
    /// <param name="host">The shell host.</param>
    public void AttachHost(IApplicationHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        _host = host;
        Publish();
    }

    /// <summary>Gets or sets the window-visibility provider the tray menu and the tray command use.</summary>
    internal Func<bool> WindowVisible { get; set; } = static () => true;

    /// <summary>
    /// Routes a shell command id (toolbar, primary menu or tray) to the intent it
    /// names. The shell raises ids; the content maps them to the semantic events.
    /// </summary>
    /// <param name="id">The command id.</param>
    public void HandleCommand(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (_readOnly &&
            id is "toggle" or "new" or "edit" or "rename" or "delete" or "paste" or "import" or "restore")
        {
            SetError(ReadOnlyMessage);
            return;
        }

        // The editor page publishes its own commands; when it is open, it owns them.
        if (id.StartsWith("editor.", StringComparison.Ordinal))
        {
            _editor?.HandleCommand(id);
            return;
        }

        if (id.StartsWith("rename.", StringComparison.Ordinal))
        {
            _rename?.HandleCommand(id);
            return;
        }

        switch (id)
        {
            case "toggle":
                ToggleSelectedTunnel();
                break;
            case "new":
                CreateRequested?.Invoke();
                break;
            case "refresh":
                RefreshRequested?.Invoke();
                break;
            case "edit":
                WithSelection(name => EditRequested?.Invoke(name));
                break;
            case "rename":
                WithSelection(name => RenameRequested?.Invoke(name));
                break;
            case "delete":
                _ = ConfirmDeleteAsync();
                break;
            case "paste":
                _ = PasteClipboardProfileAsync();
                break;
            case "import":
                _ = PickImportPathAsync();
                break;
            case "export":
                _ = PickExportPathAsync();
                break;
            case "backup":
                _ = PickBackupPathAsync();
                break;
            case "restore":
                _ = PickRestorePathAsync();
                break;
            case "tray.show":
                if (WindowVisible())
                {
                    _host?.HideWindow();
                }
                else
                {
                    _host?.ShowWindow();
                }

                Publish();
                break;
            case "tray.quit":
                _host?.Quit();
                break;
            default:
                if (id.StartsWith("connect:", StringComparison.Ordinal))
                {
                    StartRequested?.Invoke(id["connect:".Length..]);
                }
                else if (id.StartsWith("disconnect:", StringComparison.Ordinal))
                {
                    StopRequested?.Invoke(id["disconnect:".Length..]);
                }

                break;
        }
    }

    /// <summary>Turns the read-only session on or off.</summary>
    /// <param name="readOnly">Whether the session must not change anything.</param>
    public void SetReadOnly(bool readOnly)
    {
        _readOnly = readOnly;
        Publish();

        if (_detail is not null)
        {
            _detail.SetReadOnly(readOnly);
            if (_host is not null)
            {
                _detail.PublishSnapshot(_host);
            }
        }
    }

    /// <summary>Republishes the chrome and the tray from the current state.</summary>
    public void Publish()
    {
        if (_host is null)
        {
            return;
        }

        // The list is the root page: it carries no identity in the chrome, only
        // the read-only notice when the session is read-only, so the bar shows the
        // window controls, the menu and the toolbar and nothing else.
        var subtitle = _readOnly ? ReadOnlyMessage : string.Empty;
        _host.SetSnapshot(new ShellSnapshot(ProductTitle, subtitle, BuildCommands(), _busy));
        PublishTray();
    }

    /// <inheritdoc />
    public void ShowTunnels(IReadOnlyList<TunnelListItem> tunnels, string? selectedName)
    {
        ArgumentNullException.ThrowIfNull(tunnels);

        _suspendSelection = true;
        try
        {
            _tunnelList.Items.Clear();
            _rowNames.Clear();
            _tunnelStates.Clear();
            _rowTransfers.Clear();

            foreach (var tunnel in tunnels)
            {
                _rowNames.Add(tunnel.Name);
                _tunnelStates[tunnel.Name] = tunnel.State;
                _tunnelList.Items.Add(BuildTunnelRow(tunnel));
            }

            var index = selectedName is null ? -1 : _rowNames.IndexOf(selectedName);
            _tunnelList.SelectedIndex = index;
        }
        finally
        {
            _suspendSelection = false;
        }

        _emptyText.IsVisible = tunnels.Count == 0;
        _lastTunnels = tunnels;
        RefreshDetail();
        Publish();
    }

    /// <inheritdoc />
    public void ShowTransfer(TransferView transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);

        _detail?.UpdateTransfer(transfer);
    }

    /// <inheritdoc />
    public void ShowTunnelTransfers(IReadOnlyDictionary<string, TransferView> transfers)
    {
        ArgumentNullException.ThrowIfNull(transfers);

        _transfers = transfers;

        foreach (var (name, (down, up)) in _rowTransfers)
        {
            if (transfers.TryGetValue(name, out var transfer))
            {
                down.Text = string.Concat("\u2193 ", transfer.DownloadRate);
                up.Text = string.Concat("\u2191 ", transfer.UploadRate);
            }
        }

        if (_detail is not null && transfers.TryGetValue(_detail.TunnelName, out var detailTransfer))
        {
            _detail.UpdateTransfer(detailTransfer);
        }
    }

    /// <inheritdoc />
    public void ShowEditor(EditorSnapshot editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        var view = new TunnelEditorView();
        view.SaveRequested += input => SaveRequested?.Invoke(input);
        view.CancelRequested += () => EditorCancelRequested?.Invoke();
        view.PeerAddRequested += () => EditorPeerAddRequested?.Invoke();
        view.PeerRemoveRequested += index => EditorPeerRemoveRequested?.Invoke(index);
        view.KeyPairRequested += () => EditorKeyPairRequested?.Invoke();

        _editor = view;
        view.Load(editor);
        _host?.Navigation.Push(editor.Title, "Review the fields; the private key is never shown.", view);
        view.PublishSnapshot(_host!);
    }

    /// <inheritdoc />
    public void UpdateEditorKeys(bool hasPrivateKey, string? publicKey) =>
        _editor?.UpdateKeys(hasPrivateKey, publicKey);

    /// <inheritdoc />
    public void AppendEditorPeerRow(EditorPeerInput peer) => _editor?.AppendPeer(peer);

    /// <inheritdoc />
    public void RemoveEditorPeerRow(int index) => _editor?.RemovePeer(index);

    /// <inheritdoc />
    public void HideEditor()
    {
        if (_editor is null)
        {
            return;
        }

        _editor = null;
        _host?.Navigation.Pop();
    }

    /// <inheritdoc />
    public void ShowRename(TunnelRenameSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var view = new TunnelRenameView();
        view.SaveRequested += input => RenameSubmitted?.Invoke(input);
        view.CancelRequested += () => RenameCancelRequested?.Invoke();

        _rename = view;
        view.Load(snapshot);
        _host?.Navigation.Push(ProductTitle, "Change the display name; the real tunnel name never changes.", view);
        if (_host is not null)
        {
            view.PublishSnapshot(_host);
        }
    }

    /// <inheritdoc />
    public void HideRename()
    {
        if (_rename is null)
        {
            return;
        }

        _rename = null;
        _host?.Navigation.Pop();
    }

    /// <inheritdoc />
    public void SetEditorError(string fieldKey, string message) =>
        _editor?.SetEditorError(fieldKey, message);

    /// <inheritdoc />
    public void SetStatus(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        _status = message;
        Publish();
    }

    /// <inheritdoc />
    public void ShowToast(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        _host?.Notifications.Toast(message, MessageSeverity.Success);
    }

    /// <inheritdoc />
    public void SetError(string? message)
    {
        if (_host is null)
        {
            return;
        }

        if (string.IsNullOrEmpty(message))
        {
            _host.Notifications.DismissBanner();
            return;
        }

        // Errors are transient toasts in the corner, not a persistent banner.
        _host.Notifications.Toast(message, MessageSeverity.Error);
    }

    /// <inheritdoc />
    public void ShowError(CoreError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        SetError(error.Message);
    }

    /// <inheritdoc />
    public void SetBusy(bool busy)
    {
        _busy = busy;
        BusyChanged?.Invoke(busy);
        Publish();
    }

    /// <inheritdoc />
    public async Task<QuitChoice> ConfirmQuitAsync(IReadOnlyList<string> connectedNames, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connectedNames);

        var host = _host;
        if (host is null)
        {
            return QuitChoice.Cancel;
        }

        var result = await host.Dialogs.ConfirmAsync(
            new ConfirmRequest(
                "Quit with a VPN connected?",
                $"Still connected: {string.Join(", ", connectedNames)}.",
                "Disconnect and quit",
                "Stay connected and quit"),
            cancellationToken).ConfigureAwait(true);

        return result switch
        {
            ConfirmResult.Accept => QuitChoice.DisconnectAndQuit,
            ConfirmResult.Alternative => QuitChoice.StayConnectedAndQuit,
            _ => QuitChoice.Cancel,
        };
    }

    /// <inheritdoc />
    public void ShowWindow() => _host?.ShowWindow();

    private CommandSet BuildCommands()
    {
        var canMutate = !_busy && !_readOnly;
        var selected = CurrentSelection();
        var up = string.Equals(SelectedState(), "Up", StringComparison.Ordinal);

        return new CommandSet(
        [
            new AppCommand("refresh", "Refresh", IconKey: "WgmRefresh", ToolTip: "Re-read the tunnel list", Shortcut: "Ctrl+R", IsEnabled: !_busy, Group: "view"),
            new AppCommand("toggle", up ? "Disconnect" : "Connect", IconKey: up ? "WgmDisconnect" : "WgmConnect", ToolTip: up ? "Disconnect the selected tunnel" : "Connect the selected tunnel", IsEnabled: canMutate && selected is not null, IsChecked: up, Group: "selected"),
            new AppCommand("rename", "Rename", IconKey: "WgmRename", ToolTip: "Change the tunnel's display name", IsEnabled: canMutate && selected is not null, Group: "selected"),
        ]);
    }

    private void PublishTray()
    {
        // Only reached from Publish, which already returned when there is no host.
        IReadOnlyList<TunnelListItem> tunnels = _readOnly ? [] : _lastTunnels;
        _host!.Tray.Show(ProductTitle, BuildTrayCommands(TrayMenuBuilder.Build(tunnels, WindowVisible())));
    }

    /// <summary>Maps the tray menu entries onto the shell's command model.</summary>
    /// <param name="items">The tray entries.</param>
    /// <returns>The tray command set.</returns>
    internal static CommandSet BuildTrayCommands(IReadOnlyList<TrayMenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var commands = new List<AppCommand>();

        foreach (var item in items)
        {
            if (item.IsSeparator)
            {
                continue;
            }

            commands.Add(item.Action switch
            {
                TrayMenuAction.ToggleWindow => new AppCommand("tray.show", item.Header, Group: "window"),
                TrayMenuAction.Connect => new AppCommand(string.Concat("connect:", item.TunnelName), item.Header, Group: "tunnels"),
                TrayMenuAction.Disconnect => new AppCommand(string.Concat("disconnect:", item.TunnelName), item.Header, Group: "tunnels"),
                TrayMenuAction.Quit => new AppCommand("tray.quit", item.Header, Group: "quit"),
                _ => new AppCommand("tray.noop", item.Header, IsEnabled: false, Group: "window"),
            });
        }

        return new CommandSet(commands);
    }

    private Grid BuildTunnelRow(TunnelListItem tunnel)
    {
        var identity = new StackPanel { Spacing = 2 };
        identity.Children.Add(new TextBlock { Text = tunnel.ShownName, FontWeight = FontWeight.SemiBold });
        identity.Children.Add(new TextBlock
        {
            Text = string.Concat(tunnel.Configuration, " \u00b7 ", tunnel.Peers),
            Classes = { "muted" },
        });

        if (tunnel.Note is not null)
        {
            identity.Children.Add(new TextBlock
            {
                Text = tunnel.Note,
                Classes = { "error" },
                TextWrapping = TextWrapping.Wrap,
            });
        }

        var hasTransfer = _transfers.TryGetValue(tunnel.Name, out var transfer);
        var down = new TextBlock
        {
            Text = string.Concat("\u2193 ", hasTransfer ? transfer!.DownloadRate : "0 B/s"),
            Classes = { "muted" },
        };
        var up = new TextBlock
        {
            Text = string.Concat("\u2191 ", hasTransfer ? transfer!.UploadRate : "0 B/s"),
            Classes = { "muted" },
        };
        _rowTransfers[tunnel.Name] = (down, up);

        var chip = new StatusChip();
        chip.Show(tunnel.State);

        // The state and the live rate sit together on the right; the identity stays on
        // the left.
        var state = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { down, up, chip },
        };

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(identity, 0);
        Grid.SetColumn(state, 1);
        row.Children.Add(identity);
        row.Children.Add(state);
        return row;
    }

    private void OnTunnelSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suspendSelection)
        {
            return;
        }

        // Selecting a row never navigates: it only publishes the selection, so the
        // toolbar and the menu act on it. The detail page opens on a double-click or
        // on Enter.
        Publish();

        if (CurrentSelection() is not { } name)
        {
            return;
        }

        TunnelSelected?.Invoke(name);
    }

    private void OnTunnelDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (CurrentSelection() is { } name)
        {
            OpenDetail(name);
        }
    }

    private void OnTunnelKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && CurrentSelection() is { } name)
        {
            OpenDetail(name);
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.TryGetFiles()?.Any(IsProfile) == true ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        var paths = e.DataTransfer.TryGetFiles()?
            .Select(item => item.TryGetLocalPath())
            .Where(path => path is not null)
            .Select(path => path!)
            .ToArray() ?? [];

        HandleDrop(paths);
    }

    private static bool IsProfile(IStorageItem item) =>
        item.Name.EndsWith(".conf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Imports each dropped profile path; anything but a <c>.conf</c> is ignored.</summary>
    /// <param name="paths">The dropped file paths.</param>
    internal void HandleDrop(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        foreach (var path in paths.Where(path => path.EndsWith(".conf", StringComparison.OrdinalIgnoreCase)))
        {
            ImportRequested?.Invoke(path);
        }
    }

    /// <summary>Opens the detail page for a tunnel, or updates the open one.</summary>
    /// <param name="name">The tunnel name.</param>
    internal void OpenDetail(string name)
    {
        if (_host is null)
        {
            return;
        }

        if (_detail is not null)
        {
            if (string.Equals(_detail.TunnelName, name, StringComparison.Ordinal))
            {
                return;
            }

            _host.Navigation.Pop();
        }

        var item = _lastTunnels.FirstOrDefault(tunnel => string.Equals(tunnel.Name, name, StringComparison.Ordinal));
        if (item is null)
        {
            return;
        }

        var view = new TunnelDetailView(item, TransferFor(name), _readOnly);
        view.DetachedFromVisualTree += (_, _) => OnDetailDetached(view);
        _detail = view;
        _host.Navigation.Push(item.ShownName, string.Concat(item.Configuration, "  \u00b7  ", item.Peers), view);
        view.PublishSnapshot(_host);
    }

    /// <summary>Handles a detail page leaving the visual tree: a pop clears it.</summary>
    /// <param name="view">The detail page that detached.</param>
    internal void OnDetailDetached(TunnelDetailView view)
    {
        // The editor is pushed on top of the detail, which detaches it without
        // closing it; only a real pop clears the page and the selection.
        if (!ReferenceEquals(_detail, view) || _editor is not null)
        {
            return;
        }

        _detail = null;
        _suspendSelection = true;
        _tunnelList.SelectedIndex = -1;
        _suspendSelection = false;
    }

    private void RefreshDetail()
    {
        if (_detail is null || _editor is not null)
        {
            return;
        }

        var item = _lastTunnels.FirstOrDefault(tunnel => string.Equals(tunnel.Name, _detail.TunnelName, StringComparison.Ordinal));
        if (item is not null)
        {
            _detail.UpdateItem(item);
        }
        else
        {
            // The tunnel disappeared from the list (deleted, renamed): close its page.
            _host?.Navigation.Pop();
        }
    }

    private TransferView TransferFor(string name) =>
        _transfers.TryGetValue(name, out var transfer) ? transfer : ZeroTransfer;

    private void ToggleSelectedTunnel()
    {
        if (CurrentSelection() is not { } name)
        {
            SetError("Select a tunnel first.");
            return;
        }

        if (string.Equals(SelectedState(), "Up", StringComparison.Ordinal))
        {
            StopRequested?.Invoke(name);
            return;
        }

        StartRequested?.Invoke(name);
    }

    private string? SelectedState()
    {
        var name = CurrentSelection();
        return name is not null && _tunnelStates.TryGetValue(name, out var state) ? state : null;
    }

    private string? CurrentSelection()
    {
        var index = _tunnelList.SelectedIndex;
        return index >= 0 && index < _rowNames.Count ? _rowNames[index] : null;
    }

    private void WithSelection(Action<string> action)
    {
        if (CurrentSelection() is { } name)
        {
            action(name);
            return;
        }

        SetError("Select a tunnel first.");
    }

    private async Task ConfirmDeleteAsync()
    {
        if (CurrentSelection() is not { } name)
        {
            SetError("Select a tunnel first.");
            return;
        }

        var host = _host;
        if (host is null)
        {
            return;
        }

        var answer = await host.Dialogs.ConfirmAsync(
            new ConfirmRequest(
                $"Delete '{name}'?",
                "This removes the tunnel configuration from disk. A running tunnel is not stopped first.",
                "Delete",
                Destructive: true)).ConfigureAwait(true);

        if (answer == ConfirmResult.Accept)
        {
            DeleteRequested?.Invoke(name);
        }
    }

    private async Task PickBackupPathAsync()
    {
        try
        {
            if (await PickSaveAsync("Back up the application state", "vpn-manager-state.json", ".json").ConfigureAwait(true) is { } path)
            {
                StateBackupRequested?.Invoke(path);
            }
        }
        catch (Exception exception)
        {
            SetError(exception.Message);
        }
    }

    private async Task PickRestorePathAsync()
    {
        try
        {
            if (await PickOpenAsync("Restore the application state", ".json").ConfigureAwait(true) is { } path)
            {
                StateRestoreRequested?.Invoke(path);
            }
        }
        catch (Exception exception)
        {
            SetError(exception.Message);
        }
    }

    private async Task PasteClipboardProfileAsync()
    {
        try
        {
            var text = await ReadClipboardTextAsync().ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(text))
            {
                SetError("The clipboard holds no text to paste as a profile.");
                return;
            }

            ProfileTextPasted?.Invoke(text);
        }
        catch (Exception exception)
        {
            SetError(exception.Message);
        }
    }

    private async Task PickImportPathAsync()
    {
        try
        {
            if (await PickOpenAsync("Import a WireGuard configuration", ".conf").ConfigureAwait(true) is { } path)
            {
                ImportRequested?.Invoke(path);
            }
        }
        catch (Exception exception)
        {
            SetError(exception.Message);
        }
    }

    private async Task<string?> PickOpenAsync(string title, string extension)
    {
        var filters = new[] { new FileTypeFilter(extension, [string.Concat("*", extension)]) };
        var files = await Picker.OpenFilesAsync(new FilePickerOptions(title, false, filters)).ConfigureAwait(true);
        return files.Count > 0 ? files[0] : null;
    }

    private async Task<string?> PickSaveAsync(string title, string suggestedFileName, string extension)
    {
        var filters = new[] { new FileTypeFilter(extension, [string.Concat("*", extension)]) };
        return await Picker.SaveFileAsync(new SaveFilePickerOptions(title, suggestedFileName, filters)).ConfigureAwait(true);
    }

    private async Task<string?> ReadClipboardTextAsync() =>
        _host is null ? null : await _host.Clipboard.GetTextAsync().ConfigureAwait(true);

    private async Task PickExportPathAsync()
    {
        if (CurrentSelection() is not { } name)
        {
            SetError("Select a tunnel before exporting.");
            return;
        }

        try
        {
            if (await PickSaveAsync("Export the WireGuard configuration", string.Concat(name, ".conf"), ".conf").ConfigureAwait(true) is { } path)
            {
                ExportRequested?.Invoke(path);
            }
        }
        catch (Exception exception)
        {
            SetError(exception.Message);
        }
    }
}
