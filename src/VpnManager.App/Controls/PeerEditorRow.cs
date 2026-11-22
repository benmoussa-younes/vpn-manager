using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VpnManager.Core.Presentation;

namespace VpnManager.App.Controls;

/// <summary>
/// One editable peer in the tunnel editor: six labelled fields in a two-column
/// grid, plus a remove button. The fields stack vertically so the row stretches to
/// the editor width instead of forcing a horizontal scroll. It only carries text in
/// and out; the presenter validates and decides.
/// </summary>
public sealed class PeerEditorRow : UserControl
{
    private readonly TextBox _label;
    private readonly TextBox _publicKey;
    private readonly TextBox _presharedKey;
    private readonly TextBox _allowedIps;
    private readonly TextBox _endpoint;
    private readonly TextBox _keepalive;
    private readonly Button _remove;

    /// <summary>Initializes an empty peer row.</summary>
    public PeerEditorRow()
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"),
            ColumnSpacing = 12,
            RowSpacing = 8,
        };

        _label = AddField(grid, 0, 0, "Label", "Home");
        _publicKey = AddField(grid, 0, 1, "Public key", "Base64 public key");
        _presharedKey = AddField(grid, 1, 0, "Preshared key (optional)", "Leave blank for none");
        _allowedIps = AddField(grid, 1, 1, "Allowed IPs", "One per line, e.g. 0.0.0.0/0", multiline: true);
        _endpoint = AddField(grid, 2, 0, "Endpoint", "host:port");
        _keepalive = AddField(grid, 2, 1, "Keepalive (seconds)", "25");

        _remove = new Button
        {
            Content = "Remove peer",
            Classes = { "destructive" },
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        _remove.Click += (_, _) => RemoveRequested?.Invoke();
        Grid.SetRow(_remove, 3);
        Grid.SetColumn(_remove, 1);
        grid.Children.Add(_remove);

        foreach (var box in new[] { _label, _publicKey, _presharedKey, _allowedIps, _endpoint, _keepalive })
        {
            box.TextChanged += (_, _) =>
            {
                if (!_loading)
                {
                    Changed?.Invoke();
                }
            };
        }

        Content = grid;
    }

    /// <summary>Raised when the operator asked to remove this row.</summary>
    public event Action? RemoveRequested;

    /// <summary>Raised when a field of this row changed.</summary>
    public event Action? Changed;

    private bool _loading;

    /// <summary>Gets the remove button, for tests.</summary>
    internal Button RemoveButton => _remove;

    /// <summary>Sets every field from the peer input.</summary>
    /// <param name="peer">The peer values to show.</param>
    public void Initialize(EditorPeerInput peer)
    {
        ArgumentNullException.ThrowIfNull(peer);

        _loading = true;
        try
        {
            _label.Text = peer.Label;
            _publicKey.Text = peer.PublicKey;
            _presharedKey.Text = peer.PresharedKey;
            _allowedIps.Text = peer.AllowedIps;
            _endpoint.Text = peer.Endpoint;
            _keepalive.Text = peer.PersistentKeepalive;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Reads every field back as a peer input.</summary>
    /// <returns>The values as they are in the fields.</returns>
    public EditorPeerInput Read() => new(
        _label.Text ?? string.Empty,
        _publicKey.Text ?? string.Empty,
        _presharedKey.Text ?? string.Empty,
        _allowedIps.Text ?? string.Empty,
        _endpoint.Text ?? string.Empty,
        _keepalive.Text ?? string.Empty);

    /// <summary>Gets the field control for a validation key, or <see langword="null"/>.</summary>
    /// <param name="fieldKey">One of the <see cref="EditorField"/> constants.</param>
    /// <returns>The control, or <see langword="null"/> when the key is not a peer field.</returns>
    public TextBox? Field(string fieldKey) => fieldKey switch
    {
        EditorField.PeerPublicKey => _publicKey,
        EditorField.PeerPresharedKey => _presharedKey,
        EditorField.PeerAllowedIps => _allowedIps,
        EditorField.PeerEndpoint => _endpoint,
        EditorField.PeerKeepalive => _keepalive,
        _ => null,
    };

    private static TextBox AddField(Grid grid, int row, int column, string caption, string placeholder, bool multiline = false)
    {
        var box = new TextBox
        {
            PlaceholderText = placeholder,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multiline ? 64 : 0,
        };

        var label = new TextBlock
        {
            Text = caption,
            Classes = { "muted" },
        };
        AutomationProperties.SetLabeledBy(box, label);

        var panel = new StackPanel
        {
            Spacing = 4,
            Children = { label, box },
        };

        Grid.SetRow(panel, row);
        Grid.SetColumn(panel, column);
        grid.Children.Add(panel);
        return box;
    }
}
