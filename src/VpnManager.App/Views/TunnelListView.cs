using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Core.Mvp.Composition;
using Core.Mvp.Presentation;
using Core.Mvp.Services;
using Core.Mvp.Views;
using Core.Diagnostics;
using Core.Results;
using VpnManager.App.Controls;
using VpnManager.Core.Model;
using VpnManager.Core.Presentation;

namespace VpnManager.App.Views;

/// <summary>
/// The master-detail surface (MVP, passive view): a persistent sidebar of
/// profiles and, on selection, the profile's editable pane. The pane embeds the
/// configuration form, the policy card and the display name; the empty state and
/// the drag-and-drop import stay at the surface level. The view holds no rules
/// about tunnels, keys or files.
/// </summary>
public sealed class TunnelListView : UserControl, ITunnelListView, IBusyView
{
    private const string ReadOnlyMessage =
        "Read-only session: creating, editing, deleting and starting or stopping tunnels is disabled.";

    private const string EmptyHeading = "No tunnels yet";

    private const string EmptyDetail = "Import a WireGuard profile to get started.";

    private const string EmptyAction = "Import a profile\u2026";

    /// <summary>The product name shown in the header bar and the window title.</summary>
    internal const string ProductTitle = "VPN Manager";

    private static readonly TransferView ZeroTransfer = new("0 B/s", "0 B", "0 B/s", "0 B");

    private readonly ListBox _tunnelList = new() { SelectionMode = SelectionMode.Single, Background = Brushes.Transparent };
    private readonly EmptyState _emptyState;
    private readonly TunnelEditorView _editor = new();
    private readonly TunnelPolicyView _policyView = new();
    private readonly TextBox _displayName = new() { PlaceholderText = "Leave blank to show the real name" };
    private readonly TextBlock _paneName = new() { Classes = { "heading" } };
    private readonly TextBlock _paneSource = new() { Classes = { "muted" } };
    private readonly TextBlock _paneState = new();
    private readonly TextBlock _paneDown = new() { Classes = { "muted" } };
    private readonly TextBlock _paneUp = new() { Classes = { "muted" } };
    private readonly Button _connect = new() { Classes = { "primary" }, Content = "Connect" };
    private readonly Button _save = new() { Classes = { "primary" }, Content = "Save" };
    private readonly Button _cancel = new() { Content = "Cancel" };
    private readonly Button _export = new() { Content = "Export" };
    private readonly Button _delete = new() { Classes = { "destructive" }, Content = "Delete" };
    private readonly Border _displayCard;
    private readonly ScrollViewer _pane;
    private readonly StackPanel _editActions;
    private readonly Grid _columns;
    private readonly StackPanel _rightColumn;
    private bool _wideLayout = true;
    private readonly List<string> _rowNames = [];
    private readonly Dictionary<string, string> _tunnelStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (TextBlock Down, TextBlock Up)> _rowTransfers = new(StringComparer.Ordinal);
    private IApplicationHost? _host;
    private IFilePicker? _picker;
    private IReadOnlyList<TunnelListItem> _lastTunnels = [];
    private IReadOnlyDictionary<string, TransferView> _transfers = new Dictionary<string, TransferView>(StringComparer.Ordinal);
    private string? _selectedName;
    private string _status = string.Empty;
    private bool _suspendSelection;
    private bool _readOnly;
    private bool _busy;
    private bool _dirty;
    private bool _editorIsNew;

    /// <summary>Initializes the master-detail surface: the profile sidebar and the editor pane.</summary>
    public TunnelListView()
    {
        _tunnelList.SelectionChanged += OnTunnelSelectionChanged;

        // A `.conf` dropped anywhere on the page is imported.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        _emptyState = new EmptyState();
        _emptyState.Configure(EmptyHeading, EmptyDetail, EmptyAction);
        _emptyState.ActionRequested += () => HandleCommand("import");
        _emptyState.IsVisible = true;

        // The editor and the policy card are embedded, not pushed as pages.
        _editor.SaveRequested += input => SaveRequested?.Invoke(input);
        _editor.CancelRequested += () => EditorCancelRequested?.Invoke();
        _editor.PeerAddRequested += () => EditorPeerAddRequested?.Invoke();
        _editor.PeerRemoveRequested += index => EditorPeerRemoveRequested?.Invoke(index);
        _editor.KeyPairRequested += () => EditorKeyPairRequested?.Invoke();
        _editor.Changed += () => MarkDirty(true);
        _editor.IsVisible = false;
        _policyView.Changed += () =>
        {
            if (!_readOnly)
            {
                PolicySubmitted?.Invoke(_policyView.ReadInput());
            }
        };
        _policyView.IsVisible = false;

        _displayName.LostFocus += (_, _) => CommitDisplayName();
        _displayName.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                CommitDisplayName();
            }
        };
        _displayCard = Card("Display name", _displayName);
        _displayCard.IsVisible = false;

        _connect.Click += (_, _) => ToggleSelectedTunnel();
        _save.Click += (_, _) => _editor.HandleCommand("editor.save");
        _cancel.Click += (_, _) => _editor.HandleCommand("editor.cancel");
        _export.Click += (_, _) => HandleCommand("export");
        _delete.Click += (_, _) => HandleCommand("delete");

        var footerLeft = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { _export, _delete },
        };
        var footerRight = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { _cancel, _save },
        };
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(footerRight, 1);
        footer.Children.Add(footerLeft);
        footer.Children.Add(footerRight);

        var footerRule = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 12, 0, 0),
            Child = footer,
        };
        ThemeResources.Bind(footerRule, Border.BorderBrushProperty, "BorderBrush");
        _editActions = new StackPanel { Children = { footerRule } };

        var paneHeader = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12,
        };
        var headerLeft = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                _paneName,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Children = { _paneState, _paneSource },
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    Children = { _paneDown, _paneUp },
                },
            },
        };
        _connect.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(_connect, 1);
        paneHeader.Children.Add(headerLeft);
        paneHeader.Children.Add(_connect);

        // Column 0 edits the profile, column 1 manages its peers. The peer card is
        // owned by the editor but hosted here, so each column keeps its own width.
        var leftColumn = new StackPanel
        {
            Spacing = 16,
            Children = { _displayCard, _editor, _policyView },
        };
        _rightColumn = new StackPanel
        {
            Spacing = 16,
            Children = { _editor.PeersSection },
        };
        _columns = new Grid();
        Grid.SetColumn(leftColumn, 0);
        Grid.SetColumn(_rightColumn, 1);
        _columns.Children.Add(leftColumn);
        _columns.Children.Add(_rightColumn);
        ApplyColumns(wide: true);

        var paneContent = new StackPanel
        {
            Spacing = 16,
            Margin = new Thickness(24),
            Children = { paneHeader, _columns, _editActions },
        };
        paneContent.MaxWidth = 1200;
        paneContent.HorizontalAlignment = HorizontalAlignment.Center;
        _pane = new ScrollViewer { Content = paneContent, IsVisible = false };
        _pane.SizeChanged += (_, _) => UpdateColumns(_pane.Bounds.Width);

        var newButton = new Button { Content = "New", Classes = { "flat" } };
        newButton.Click += (_, _) => CreateRequested?.Invoke();
        var refreshButton = new Button { Content = "Refresh", Classes = { "flat" } };
        refreshButton.Click += (_, _) => RefreshRequested?.Invoke();

        var sidebarActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { newButton, refreshButton },
        };
        var sidebarHeading = new TextBlock
        {
            Text = "Profiles",
            Classes = { "heading" },
            VerticalAlignment = VerticalAlignment.Center,
        };
        var sidebarHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(sidebarHeading, 0);
        Grid.SetColumn(sidebarActions, 1);
        sidebarHeader.Children.Add(sidebarHeading);
        sidebarHeader.Children.Add(sidebarActions);

        var sidebar = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 8,
        };
        Grid.SetRow(sidebarHeader, 0);
        Grid.SetRow(_tunnelList, 1);
        sidebar.Children.Add(sidebarHeader);
        sidebar.Children.Add(_tunnelList);

        var separator = new Border { Width = 1 };
        ThemeResources.Bind(separator, Border.BackgroundProperty, "BorderBrush");

        var paneHost = new Panel();
        paneHost.Children.Add(_pane);
        paneHost.Children.Add(_emptyState);

        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("300,1,*") };
        Grid.SetColumn(sidebar, 0);
        Grid.SetColumn(separator, 1);
        Grid.SetColumn(paneHost, 2);
        root.Children.Add(sidebar);
        root.Children.Add(separator);
        root.Children.Add(paneHost);

        Content = root;
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
    public event Action<string>? DeleteRequested;

    /// <inheritdoc />
    public event Action<TunnelRenameInput>? RenameSubmitted;

    /// <inheritdoc />
    public event Action<TunnelPolicyInput>? PolicySubmitted;

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

    /// <summary>Gets the centred empty state, for tests.</summary>
    internal EmptyState EmptyStateView => _emptyState;

    /// <summary>Gets the editor currently open on the shell stack, or <see langword="null"/>.</summary>
    internal TunnelEditorView? OpenEditor => _editor;

    /// <summary>Gets the embedded policy card, for tests.</summary>
    internal TunnelPolicyView PolicyView => _policyView;

    /// <summary>Gets whether the inline display-name card is shown, for tests.</summary>
    internal bool DisplayCardVisible => _displayCard.IsVisible;

    /// <summary>Gets the inline display-name box, for tests.</summary>
    internal TextBox DisplayNameBox => _displayName;

    /// <summary>Gets the inline connect button, for tests.</summary>
    internal Button ConnectButton => _connect;

    /// <summary>Gets the pane heading, for tests.</summary>
    internal TextBlock PaneNameText => _paneName;

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

    /// <summary>Gets or sets the diagnostic log, when the shell provides one.</summary>
    internal IApplicationLogger? Logger { get; set; }

    /// <summary>
    /// Routes a shell command id (toolbar or primary menu) to the intent it
    /// names. The shell raises ids; the content maps them to the semantic events.
    /// </summary>
    /// <param name="id">The command id.</param>
    public void HandleCommand(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (_readOnly &&
            id is "toggle" or "new" or "delete" or "paste" or "import" or "restore")
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
        var leftReadOnly = _readOnly && !readOnly;
        _readOnly = readOnly;

        // The empty-state action mutates the store, so it is absent in read-only.
        _emptyState.Configure(EmptyHeading, EmptyDetail, readOnly ? string.Empty : EmptyAction);

        if (leftReadOnly && _host is not null)
        {
            _host.Notifications.DismissBanner();
        }

        _editor.IsEnabled = !readOnly;
        _policyView.IsEnabled = !readOnly;
        _displayName.IsEnabled = !readOnly;
        _save.IsEnabled = !readOnly;
        _cancel.IsEnabled = !readOnly;
        _editor.PeersSection.IsEnabled = !readOnly;
        _delete.IsEnabled = !readOnly;
        UpdatePaneHeader();
        Publish();
    }

    /// <summary>Republishes the chrome from the current state.</summary>
    public void Publish()
    {
        if (_host is null)
        {
            return;
        }

        // A persistent, toned notice: the subtitle carries the text, the banner
        // carries the severity. Re-asserted on every publish, so an error toast
        // cannot silently erase it.
        if (_readOnly)
        {
            _host.Notifications.Banner(ReadOnlyMessage, MessageSeverity.Warning);
        }

        // There is no toolbar: the actions live in the sidebar and the pane. The
        // header keeps the product title and the status; the dot carries the
        // unsaved-changes state of the configuration form.
        var subtitle = _readOnly ? ReadOnlyMessage : _status;
        _host.SetSnapshot(new ShellSnapshot(ProductTitle, subtitle, CommandSet.Empty, _busy)
        {
            IsDirty = _dirty,
        });
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

        _lastTunnels = tunnels;
        _selectedName = selectedName;
        if (selectedName is null && !_editorIsNew)
        {
            _editor.IsVisible = false;
            _displayCard.IsVisible = false;
            _policyView.IsVisible = false;
            _editActions.IsVisible = false;
            _editor.PeersSection.IsVisible = false;
            _pane.IsVisible = false;
            _emptyState.IsVisible = true;
        }

        UpdatePaneHeader();
        Publish();
    }

    /// <inheritdoc />
    public void ShowTransfer(TransferView transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);

        if (_selectedName is not null)
        {
            var updated = new Dictionary<string, TransferView>(_transfers, StringComparer.Ordinal)
            {
                [_selectedName] = transfer,
            };
            _transfers = updated;
        }

        UpdatePaneHeader();
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

        UpdatePaneHeader();
    }

    /// <inheritdoc />
    public void ShowEditor(EditorSnapshot editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        _editorIsNew = editor.IsNew;
        _editor.Load(editor);
        _editor.IsVisible = true;
        _pane.IsVisible = true;
        _emptyState.IsVisible = false;
        _displayCard.IsVisible = !editor.IsNew;
        _policyView.IsVisible = !editor.IsNew;
        _editActions.IsVisible = true;
        _editor.PeersSection.IsVisible = true;
        MarkDirty(false);
        UpdatePaneHeader();
    }

    /// <inheritdoc />
    public void UpdateEditorKeys(bool hasPrivateKey, string? publicKey) =>
        _editor.UpdateKeys(hasPrivateKey, publicKey);

    /// <inheritdoc />
    public void AppendEditorPeerRow(EditorPeerInput peer) => _editor.AppendPeer(peer);

    /// <inheritdoc />
    public void RemoveEditorPeerRow(int index) => _editor.RemovePeer(index);

    /// <inheritdoc />
    public void HideEditor()
    {
        _editor.IsVisible = false;
        _editorIsNew = false;
        _displayCard.IsVisible = false;
        _policyView.IsVisible = false;
        _editActions.IsVisible = false;
        _editor.PeersSection.IsVisible = false;
        MarkDirty(false);

        if (_selectedName is null)
        {
            _pane.IsVisible = false;
            _emptyState.IsVisible = true;
        }

        UpdatePaneHeader();
    }

    /// <inheritdoc />
    public void ShowRename(TunnelRenameSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        _displayName.Text = snapshot.CurrentDisplayName;
        _displayCard.IsVisible = !_editorIsNew;
    }

    /// <inheritdoc />
    public void HideRename()
    {
        // The display name is an inline field; there is no page to close.
    }

    /// <inheritdoc />
    public void ShowPolicy(TunnelPolicySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        _policyView.Load(snapshot);
        _policyView.IsVisible = !_editorIsNew;
    }

    /// <inheritdoc />
    public void HidePolicy()
    {
        _policyView.IsVisible = false;
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

        // Errors are transient toasts in the corner, not a persistent banner, so
        // the diagnostic log is where a failure stays readable after the toast.
        Logger?.Log(LogLevel.Warning, message);
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
    public async Task<bool> ConfirmCloseAsync(IReadOnlyList<string> connectedNames, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connectedNames);

        var host = _host;
        if (host is null)
        {
            return false;
        }

        // The dialog always renders its own Cancel; a second one here would be a
        // duplicate. The alternative stays hidden.
        var result = await host.Dialogs.ConfirmAsync(
            new ConfirmRequest(
                "Disconnect the VPN and quit?",
                $"Closing VPN Manager will disconnect: {string.Join(", ", connectedNames)}.",
                "Disconnect and quit"),
            cancellationToken).ConfigureAwait(true);

        return result == ConfirmResult.Accept;
    }

    /// <inheritdoc />
    public void ShowWindow() => _host?.ShowWindow();

    private static Border Card(string heading, Control content)
    {
        var headingBlock = new TextBlock { Text = heading, Classes = { "heading" } };
        AutomationProperties.SetLabeledBy(content, headingBlock);
        var stack = new StackPanel
        {
            Spacing = 8,
            Children = { headingBlock, content },
        };

        var border = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = ThemeResources.Corner("SurfaceCornerRadius"),
            Padding = new Thickness(16),
            Child = stack,
        };
        ThemeResources.Bind(border, Border.BackgroundProperty, "SurfaceBrush");
        ThemeResources.Bind(border, Border.BorderBrushProperty, "BorderBrush");
        return border;
    }

    private void UpdateColumns(double width)
    {
        var wide = width >= 720;
        if (wide == _wideLayout)
        {
            return;
        }

        _wideLayout = wide;
        ApplyColumns(wide);
    }

    private void ApplyColumns(bool wide)
    {
        if (wide)
        {
            _columns.ColumnDefinitions = new ColumnDefinitions("6*,5*");
            _columns.RowDefinitions = new RowDefinitions("Auto");
            _columns.ColumnSpacing = 24;
            _columns.RowSpacing = 0;
            Grid.SetColumn(_rightColumn, 1);
            Grid.SetRow(_rightColumn, 0);
            return;
        }

        _columns.ColumnDefinitions = new ColumnDefinitions("1*");
        _columns.RowDefinitions = new RowDefinitions("Auto,Auto");
        _columns.ColumnSpacing = 0;
        _columns.RowSpacing = 16;
        Grid.SetColumn(_rightColumn, 0);
        Grid.SetRow(_rightColumn, 1);
    }

    private Grid BuildTunnelRow(TunnelListItem tunnel)
    {
        var name = new TextBlock
        {
            Text = tunnel.ShownName,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var chip = new StatusChip();
        chip.Show(tunnel.State);

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(name, 0);
        Grid.SetColumn(chip, 1);
        top.Children.Add(name);
        top.Children.Add(chip);

        var detail = new TextBlock
        {
            Text = string.Concat(tunnel.Configuration, " \u00b7 ", tunnel.Peers),
            Classes = { "muted" },
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var hasTransfer = _transfers.TryGetValue(tunnel.Name, out var transfer);
        var down = new TextBlock
        {
            Text = string.Concat("\u2193 ", hasTransfer ? transfer!.DownloadRate : "0 B/s"),
            Classes = { "muted" },
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var up = new TextBlock
        {
            Text = string.Concat("\u2191 ", hasTransfer ? transfer!.UploadRate : "0 B/s"),
            Classes = { "muted" },
            HorizontalAlignment = HorizontalAlignment.Right,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _rowTransfers[tunnel.Name] = (down, up);

        var rates = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 8 };
        Grid.SetColumn(down, 0);
        Grid.SetColumn(up, 1);
        rates.Children.Add(down);
        rates.Children.Add(up);

        var stack = new StackPanel { Spacing = 4, Children = { top, detail } };
        if (tunnel.Note is not null)
        {
            stack.Children.Add(new TextBlock
            {
                Text = tunnel.Note,
                Classes = { "error" },
                TextWrapping = TextWrapping.Wrap,
            });
        }

        stack.Children.Add(rates);

        // The outage note is part of the identity, above the rates: the two rate
        // labels sit on the last line, so a changing value never reflows the row.
        var row = new Grid { Children = { stack } };
        AutomationProperties.SetName(row, string.Concat(tunnel.ShownName, ", ", tunnel.State));
        return row;
    }

    private async void OnTunnelSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suspendSelection)
        {
            return;
        }

        var newName = CurrentSelection();
        if (string.Equals(newName, _selectedName, StringComparison.Ordinal))
        {
            UpdatePaneHeader();
            return;
        }

        if (_dirty && newName is not null && _selectedName is not null && _host is not null)
        {
            var previous = _selectedName;
            var previousIndex = _rowNames.IndexOf(previous);
            var choice = await _host.Dialogs.ConfirmSaveAsync(previous).ConfigureAwait(true);
            if (choice == SavePromptChoice.Save)
            {
                _editor.HandleCommand("editor.save");
                RestoreSelection(previousIndex);
                return;
            }

            if (choice == SavePromptChoice.Cancel)
            {
                RestoreSelection(previousIndex);
                return;
            }

            MarkDirty(false);
        }

        _selectedName = newName;
        if (newName is null)
        {
            HideEditor();
            return;
        }

        TunnelSelected?.Invoke(newName);
        UpdatePaneHeader();
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
    private void UpdatePaneHeader()
    {
        var item = _selectedName is null
            ? null
            : _lastTunnels.FirstOrDefault(tunnel => string.Equals(tunnel.Name, _selectedName, StringComparison.Ordinal));
        var hasSelection = item is not null;
        var up = string.Equals(item?.State, "Up", StringComparison.Ordinal);

        _paneName.Text = item?.ShownName ?? (_editorIsNew ? "New tunnel" : string.Empty);
        _paneState.Text = item?.State ?? string.Empty;
        _paneSource.Text = item is null
            ? string.Empty
            : item.Source == TunnelSource.NetworkManager ? "NetworkManager" : "wg-quick configuration";
        _connect.Content = up ? "Disconnect" : "Connect";
        _connect.IsEnabled = hasSelection && !_readOnly && !_busy;

        var transfer = _selectedName is not null && _transfers.TryGetValue(_selectedName, out var current)
            ? current
            : ZeroTransfer;
        _paneDown.Text = string.Concat("\u2193 ", transfer.DownloadRate);
        _paneUp.Text = string.Concat("\u2191 ", transfer.UploadRate);

        var visible = hasSelection || _editorIsNew;
        _pane.IsVisible = visible;
        _emptyState.IsVisible = !visible;
    }

    private void MarkDirty(bool dirty)
    {
        if (_dirty == dirty)
        {
            return;
        }

        _dirty = dirty;
        Publish();
    }

    private void CommitDisplayName()
    {
        if (_selectedName is null || _editorIsNew)
        {
            return;
        }

        RenameSubmitted?.Invoke(new TunnelRenameInput(_selectedName, _displayName.Text ?? string.Empty));
    }

    private void RestoreSelection(int index)
    {
        _suspendSelection = true;
        _tunnelList.SelectedIndex = index;
        _suspendSelection = false;
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
                "This removes the tunnel configuration from disk. Stop a running tunnel first; the application refuses to delete one that is up.",
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
