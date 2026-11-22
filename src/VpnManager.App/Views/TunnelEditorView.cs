using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Core.Mvp.Composition;
using Core.Mvp.Presentation;
using Core.Mvp.Views;
using Core.Results;
using VpnManager.App.Controls;
using VpnManager.Core.Model;
using VpnManager.Core.Presentation;

namespace VpnManager.App.Views;

/// <summary>
/// The tunnel editor as a shell page (MVP, passive view). A centred, max-width
/// column groups the fields into cards — identity and keys, interface and routing,
/// peers — on the same label grid as the detail page. The actions live in the
/// shell toolbar. It never receives a private key: the private-key box is empty
/// and means "keep the key the presenter holds".
/// </summary>
public sealed class TunnelEditorView : UserControl, IView
{
    private const double LabelColumn = 140;

    private readonly TextBox _name;
    private readonly TextBox _privateKey;
    private readonly TextBlock _publicKey;
    private readonly TextBox _addresses;
    private readonly TextBox _dns;
    private readonly TextBox _listenPort;
    private readonly TextBox _mtu;
    private readonly TextBox _table;
    private readonly ComboBox _routingMode;
    private readonly StackPanel _peers;
    private readonly List<PeerEditorRow> _peerRows = [];
    private readonly Border _error;
    private readonly TextBlock _errorText;
    private readonly StackPanel _column;
    private TextBox? _invalid;
    private string _title = string.Empty;
    private bool _loading;

    /// <summary>Initializes the editor page and wires its controls to semantic events.</summary>
    public TunnelEditorView()
    {
        _name = Field("The interface name and the file stem");
        _privateKey = Field("Leave blank to keep the current key", password: true);
        _publicKey = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Classes = { "muted" },
            VerticalAlignment = VerticalAlignment.Center,
        };
        _addresses = Field("One per line: 10.10.0.2/24", multiline: true, height: 72);
        _dns = Field("One per line: 1.1.1.1", multiline: true, height: 56);
        _listenPort = Field("ListenPort");
        _mtu = Field("MTU (1420)");
        _table = Field("Table (auto)");

        _routingMode = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new ComboBoxItem { Content = "Split (mesh only)" },
                new ComboBoxItem { Content = "Full (default route)" },
            },
        };
        ToolTip.SetTip(_routingMode, "Split routes only the peer's allowed IPs; full routes the default route through the peer");

        _peers = new StackPanel { Spacing = 8 };

        _errorText = new TextBlock { TextWrapping = TextWrapping.Wrap, Classes = { "error" } };
        _error = new Border
        {
            IsVisible = false,
            Padding = new Thickness(12, 8),
            CornerRadius = ThemeResources.Corner("SurfaceCornerRadius"),
            BorderThickness = new Thickness(1),
            Child = _errorText,
        };
        ThemeResources.Bind(_error, Border.BorderBrushProperty, "ErrorBrush");

        var options = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*") };
        Grid.SetColumn(_listenPort, 0);
        Grid.SetColumn(_mtu, 1);
        Grid.SetColumn(_table, 2);
        _mtu.Margin = new Thickness(8, 0, 8, 0);
        options.Children.Add(_listenPort);
        options.Children.Add(_mtu);
        options.Children.Add(_table);

        var generate = new Button { Content = "Generate", VerticalAlignment = VerticalAlignment.Center };
        generate.Click += (_, _) => KeyPairRequested?.Invoke();
        ToolTip.SetTip(generate, "Generate a key pair and show its public key");
        var keyRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        keyRow.Children.Add(_privateKey);
        Grid.SetColumn(generate, 1);
        keyRow.Children.Add(generate);

        var identity = Card("Identity & keys",
            Row("Name", _name),
            Row("Private key", keyRow),
            Row("Public key", _publicKey));

        var routing = Card("Interface & routing",
            Row("Addresses", _addresses),
            Row("DNS", _dns),
            Row("Options", options),
            Row("Routing", _routingMode));

        // The peers card is hosted in the pane's second column; its action sits in
        // the card header, where the peers are.
        var addPeer = new Button { Content = "Add peer", VerticalAlignment = VerticalAlignment.Center };
        addPeer.Click += (_, _) => PeerAddRequested?.Invoke();
        var peersHeading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var peersTitle = new TextBlock { Text = "Peers", Classes = { "heading" }, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(peersTitle, 0);
        Grid.SetColumn(addPeer, 1);
        peersHeading.Children.Add(peersTitle);
        peersHeading.Children.Add(addPeer);
        PeersSection = Card(peersHeading, _peers);

        _column = new StackPanel
        {
            Spacing = 16,
            Margin = new Thickness(0),
            Children = { _error, identity, routing },
        };

        // Embedded in the master-detail pane: the pane owns the scroll view and
        // the column width, so the form must not re-centre itself.
        _column.HorizontalAlignment = HorizontalAlignment.Stretch;
        Content = _column;

        _name.TextChanged += (_, _) => OnChanged();
        _privateKey.TextChanged += (_, _) => OnChanged();
        _addresses.TextChanged += (_, _) => OnChanged();
        _dns.TextChanged += (_, _) => OnChanged();
        _listenPort.TextChanged += (_, _) => OnChanged();
        _mtu.TextChanged += (_, _) => OnChanged();
        _table.TextChanged += (_, _) => OnChanged();
        _routingMode.SelectionChanged += (_, _) => OnChanged();
    }

    /// <summary>Raised when the operator asked to save the editor.</summary>
    public event Action<EditorInput>? SaveRequested;

    /// <summary>Raised when the operator dismissed the editor.</summary>
    public event Action? CancelRequested;

    /// <summary>Raised when the operator asked to add a peer row.</summary>
    public event Action? PeerAddRequested;

    /// <summary>Raised when the operator asked to remove the peer row at the index.</summary>
    public event Action<int>? PeerRemoveRequested;

    /// <summary>Gets the peers card, hosted in the pane's second column.</summary>
    internal Control PeersSection { get; }

    /// <summary>Raised when a field changed, so the pane can mark the form dirty.</summary>
    public event Action? Changed;

    /// <summary>Raised when the operator asked to generate a key pair.</summary>
    public event Action? KeyPairRequested;

    /// <summary>Gets the number of peer rows currently shown.</summary>
    public int PeerRowCount => _peerRows.Count;

    /// <summary>Gets the peer rows currently shown, for tests.</summary>
    internal IReadOnlyList<PeerEditorRow> PeerRows => _peerRows;

    /// <summary>Gets the name field, for tests.</summary>
    internal TextBox NameBox => _name;

    /// <summary>Gets the private-key field, for tests.</summary>
    internal TextBox PrivateKeyBox => _privateKey;

    /// <summary>Gets the addresses field, for tests.</summary>
    internal TextBox AddressesBox => _addresses;

    /// <summary>Gets the DNS field, for tests.</summary>
    internal TextBox DnsBox => _dns;

    /// <summary>Gets the listen-port field, for tests.</summary>
    internal TextBox ListenPortBox => _listenPort;

    /// <summary>Gets the MTU field, for tests.</summary>
    internal TextBox MtuBox => _mtu;

    /// <summary>Gets the routing-table field, for tests.</summary>
    internal TextBox TableBox => _table;

    /// <summary>Gets the routing-mode selector, for tests.</summary>
    internal ComboBox RoutingModeBox => _routingMode;

    /// <summary>Gets the public-key line, for tests.</summary>
    internal TextBlock PublicKeyText => _publicKey;

    /// <summary>Gets the error line, for tests.</summary>
    internal TextBlock ErrorText => _errorText;

    /// <summary>Sets every field of the editor from the snapshot.</summary>
    /// <param name="editor">The editor state; it contains no private key.</param>
    public void Load(EditorSnapshot editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        _loading = true;
        try
        {
            _title = editor.Title;
            _name.Text = editor.Name;
            _name.IsEnabled = editor.NameEditable;
            _privateKey.Text = string.Empty;
            _addresses.Text = editor.Addresses;
            _dns.Text = editor.Dns;
            _listenPort.Text = editor.ListenPort;
            _mtu.Text = editor.Mtu;
            _table.Text = editor.Table;
            _routingMode.SelectedIndex = editor.RoutingMode == TunnelRoutingMode.Full ? 1 : 0;
            _errorText.Text = string.Empty;
            _error.IsVisible = false;
            ClearInvalid();

            _peers.Children.Clear();
            _peerRows.Clear();
            foreach (var peer in editor.Peers)
            {
                AppendPeer(peer);
            }

            UpdateKeys(editor.HasPrivateKey, editor.PublicKey);
        }
        finally
        {
            _loading = false;
        }

        _name.Focus();
    }

    private void OnChanged()
    {
        if (!_loading)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Updates only the key identity line, so a generation keeps the rest.</summary>
    /// <param name="hasPrivateKey">Whether the presenter now holds a private key.</param>
    /// <param name="publicKey">The derived public key, or <see langword="null"/>.</param>
    public void UpdateKeys(bool hasPrivateKey, string? publicKey)
    {
        _publicKey.Text = publicKey is null ? "Not available yet." : publicKey;
        _privateKey.PlaceholderText = hasPrivateKey
            ? "A key is held; type here only to replace it"
            : "Required";
    }

    /// <summary>Appends a peer row.</summary>
    /// <param name="peer">The row to initialize.</param>
    public void AppendPeer(EditorPeerInput peer)
    {
        ArgumentNullException.ThrowIfNull(peer);

        var row = new PeerEditorRow();
        row.Initialize(peer);
        row.RemoveRequested += () =>
        {
            var index = _peerRows.IndexOf(row);
            if (index >= 0)
            {
                PeerRemoveRequested?.Invoke(index);
            }
        };

        row.Changed += OnChanged;
        _peerRows.Add(row);
        _peers.Children.Add(row);
    }

    /// <summary>Removes the peer row at the index.</summary>
    /// <param name="index">The row index.</param>
    public void RemovePeer(int index)
    {
        if (index < 0 || index >= _peerRows.Count)
        {
            return;
        }

        _peers.Children.Remove(_peerRows[index]);
        _peerRows.RemoveAt(index);
    }

    /// <summary>Marks the offending field and shows the message.</summary>
    /// <param name="fieldKey">One of the <see cref="EditorField"/> constants.</param>
    /// <param name="message">The message; an empty message clears the marker.</param>
    public void SetEditorError(string fieldKey, string message)
    {
        ArgumentNullException.ThrowIfNull(fieldKey);

        _errorText.Text = message;
        _error.IsVisible = message.Length > 0;
        ClearInvalid();

        if (message.Length == 0)
        {
            return;
        }

        var control = ResolveField(fieldKey);
        if (control is null)
        {
            return;
        }

        control.Classes.Set("invalid", true);
        _invalid = control;
        control.Focus();
    }

    /// <summary>Reads every field back as the save input.</summary>
    /// <returns>The values as they are in the fields.</returns>
    public EditorInput ReadInput() => new(
        _name.Text ?? string.Empty,
        _privateKey.Text ?? string.Empty,
        _addresses.Text ?? string.Empty,
        _dns.Text ?? string.Empty,
        _listenPort.Text ?? string.Empty,
        _mtu.Text ?? string.Empty,
        _table.Text ?? string.Empty,
        _routingMode.SelectedIndex == 1 ? TunnelRoutingMode.Full : TunnelRoutingMode.Split,
        _peerRows.Select(row => row.Read()).ToArray());

    /// <summary>Handles one of the editor's own shell command ids.</summary>
    /// <param name="id">The command id.</param>
    /// <returns><see langword="true"/> when the id was one of the editor's.</returns>
    public bool HandleCommand(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        switch (id)
        {
            case "editor.save":
                SaveRequested?.Invoke(ReadInput());
                return true;
            case "editor.cancel":
                CancelRequested?.Invoke();
                return true;
            case "editor.addPeer":
                PeerAddRequested?.Invoke();
                return true;
            case "editor.generate":
                KeyPairRequested?.Invoke();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Publishes the editor's chrome and commands to the shell.</summary>
    /// <param name="host">The shell host.</param>
    public void PublishSnapshot(IViewHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        host.SetSnapshot(new ShellSnapshot(
            _title,
            "The private key is never shown.",
            new CommandSet(
            [
                new AppCommand("editor.save", "Save", IconKey: "IconSave", ToolTip: "Save the tunnel", Shortcut: "Ctrl+S", Group: "primary"),
                new AppCommand("editor.generate", "Generate key pair", IconKey: "WgmKey", ToolTip: "Generate a key pair", Group: "keys"),
                new AppCommand("editor.addPeer", "Add peer", IconKey: "IconRowInsert", ToolTip: "Add a peer", Group: "peers"),
                new AppCommand("editor.cancel", "Cancel", IconKey: "IconClose", ToolTip: "Cancel", Shortcut: "Escape", Group: "dismiss"),
            ])));
    }

    /// <inheritdoc />
    public void SetBusy(bool busy)
    {
        // The editor is a short form; the shell's busy overlay is driven by the
        // presenter's overall operation, not by the page.
    }

    /// <inheritdoc />
    public void ShowError(CoreError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        SetEditorError(EditorField.General, error.Message);
    }

    private static TextBox Field(string placeholder, bool password = false, bool multiline = false, double height = 0)
    {
        var box = new TextBox
        {
            PlaceholderText = placeholder,
            PasswordChar = password ? '\u2022' : '\0',
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
        };

        if (height > 0)
        {
            box.Height = height;
        }

        return box;
    }

    private static Border Card(string heading, params Control[] rows) =>
        Card(
            new TextBlock
            {
                Text = heading,
                Classes = { "heading" },
            },
            rows);

    private static Border Card(Control heading, params Control[] rows)
    {
        var stack = new StackPanel
        {
            Spacing = 12,
            Children = { heading },
        };

        foreach (var row in rows)
        {
            stack.Children.Add(row);
        }

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

    private static Grid Row(string label, Control control)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{LabelColumn},*") };
        var caption = new TextBlock
        {
            Text = label,
            Classes = { "muted" },
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetLabeledBy(control, caption);
        Grid.SetColumn(caption, 0);
        Grid.SetColumn(control, 1);
        grid.Children.Add(caption);
        grid.Children.Add(control);
        return grid;
    }

    private TextBox? ResolveField(string fieldKey)
    {
        var firstPeer = _peerRows.Count > 0 ? _peerRows[0] : null;

        return fieldKey switch
        {
            EditorField.Name => _name,
            EditorField.PrivateKey => _privateKey,
            EditorField.Addresses => _addresses,
            EditorField.Dns => _dns,
            EditorField.ListenPort => _listenPort,
            EditorField.Mtu => _mtu,
            EditorField.Table => _table,
            _ => firstPeer?.Field(fieldKey),
        };
    }

    private void ClearInvalid()
    {
        _invalid?.Classes.Set("invalid", false);
        _invalid = null;
    }
}
