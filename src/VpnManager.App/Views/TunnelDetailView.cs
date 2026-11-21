using Avalonia;
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
/// One tunnel's overview, pushed as a page when its row is activated. A centred,
/// max-width column holds a header card, a details card and a transfer card, so
/// the identity, the facts and the live rates line up on the same grid. The
/// actions live in the shell toolbar.
/// </summary>
public sealed class TunnelDetailView : UserControl, IView
{
    private const double LabelColumn = 140;

    private readonly TextBlock _heading;
    private readonly StatusChip _stateChip = new();
    private readonly TextBlock _origin;
    private readonly Border _note;
    private readonly TextBlock _noteText;
    private readonly TextBlock _stateValue;
    private readonly TextBlock _sourceValue;
    private readonly TextBlock _peersValue;
    private readonly TextBlock _configurationValue;
    private readonly TextBlock _downloadRate;
    private readonly TextBlock _downloadTotal;
    private readonly TextBlock _uploadRate;
    private readonly TextBlock _uploadTotal;
    private readonly StackPanel _column;
    private bool _readOnly;
    private TunnelListItem _item;

    /// <summary>Initializes the detail page for one tunnel.</summary>
    /// <param name="item">The tunnel row the operator activated.</param>
    /// <param name="initialTransfer">The tunnel's current transfer.</param>
    /// <param name="readOnly">Whether the session must not change anything.</param>
    public TunnelDetailView(TunnelListItem item, TransferView initialTransfer, bool readOnly)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(initialTransfer);

        _item = item;
        _readOnly = readOnly;

        _heading = new TextBlock { Classes = { "heading" } };
        _origin = new TextBlock { Classes = { "subtitle" } };

        var chips = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _stateChip, _origin },
        };

        var headerContent = new StackPanel
        {
            Spacing = 6,
            Children = { _heading, chips },
        };

        _noteText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _note = new Border
        {
            IsVisible = false,
            Padding = new Thickness(12, 8),
            CornerRadius = ThemeResources.Corner("SurfaceCornerRadius"),
            BorderThickness = new Thickness(1),
            Child = _noteText,
        };
        ThemeResources.Bind(_note, Border.BorderBrushProperty, "ErrorBrush");

        _stateValue = Value();
        _sourceValue = Value();
        _peersValue = Value();
        _configurationValue = Value();

        var details = Card(Rows(
            DetailRow("State", _stateValue),
            DetailRow("Source", _sourceValue),
            DetailRow("Peers", _peersValue),
            DetailRow("Configuration", _configurationValue)));

        _downloadRate = new TextBlock();
        _downloadTotal = new TextBlock();
        _uploadRate = new TextBlock();
        _uploadTotal = new TextBlock();

        var transfer = Card(Rows(
            RateRow("Download", "\u2193", _downloadRate, _downloadTotal),
            RateRow("Upload", "\u2191", _uploadRate, _uploadTotal)));

        _column = new StackPanel
        {
            Spacing = 16,
            Margin = new Thickness(24),
            Children = { Card(headerContent), _note, details, transfer },
        };

        // A fixed centre column flanked by two stars keeps the max width without
        // collapsing to the content width the way a centred auto column does.
        var centering = new Grid { ColumnDefinitions = new ColumnDefinitions("*,720,*") };
        Grid.SetColumn(_column, 1);
        centering.Children.Add(_column);

        Content = new ScrollViewer { Content = centering };

        ApplyItem(item);
        UpdateTransfer(initialTransfer);
    }

    /// <summary>Gets the tunnel this page shows.</summary>
    public string TunnelName => _item.Name;

    /// <summary>Gets the centred content column, for layout tests.</summary>
    internal StackPanel Column => _column;

    /// <summary>Gets the download-rate label, for tests.</summary>
    internal TextBlock DownloadRateText => _downloadRate;

    /// <summary>Gets the origin line, for tests.</summary>
    internal TextBlock OriginText => _origin;

    /// <summary>Gets the first detail value, for alignment tests.</summary>
    internal TextBlock StateValueText => _stateValue;

    /// <summary>Replaces the tunnel row from a refreshed list.</summary>
    /// <param name="item">The refreshed row.</param>
    public void UpdateItem(TunnelListItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        _item = item;
        ApplyItem(item);
    }

    /// <summary>Updates whether the session may change anything.</summary>
    /// <param name="readOnly">Whether the session is read-only.</param>
    public void SetReadOnly(bool readOnly) => _readOnly = readOnly;

    /// <summary>Replaces the live transfer.</summary>
    /// <param name="transfer">The formatted rates and totals.</param>
    public void UpdateTransfer(TransferView transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);

        _downloadRate.Text = transfer.DownloadRate;
        _downloadTotal.Text = transfer.DownloadTotal;
        _uploadRate.Text = transfer.UploadRate;
        _uploadTotal.Text = transfer.UploadTotal;
    }

    /// <summary>Publishes the page's chrome and commands to the shell.</summary>
    /// <param name="host">The shell host.</param>
    public void PublishSnapshot(IViewHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        var canMutate = !_readOnly;
        var isForeign = _item.Source == TunnelSource.NetworkManager;
        var isUp = string.Equals(_item.State, "Up", StringComparison.Ordinal);

        host.SetSnapshot(new ShellSnapshot(
            _item.ShownName,
            string.Concat(_item.Configuration, "  \u00b7  ", _item.Peers),
            new CommandSet(
            [
                new AppCommand("toggle", isUp ? "Disconnect" : "Connect", IconKey: isUp ? "WgmDisconnect" : "WgmConnect", ToolTip: isUp ? "Disconnect this tunnel" : "Connect this tunnel", IsChecked: isUp, IsEnabled: canMutate, Group: "connect"),
                new AppCommand("rename", "Rename", IconKey: "WgmRename", ToolTip: "Change this tunnel's display name", IsEnabled: canMutate, Group: "manage"),
                new AppCommand("policy", "Policy", IconKey: "WgmPolicy", ToolTip: "Reconnection and DNS policy", IsEnabled: canMutate, Group: "manage"),
                new AppCommand("delete", "Delete", IconKey: "WgmDelete", ToolTip: "Delete this tunnel", IsEnabled: canMutate && !isForeign, Group: "manage"),
                new AppCommand("export", "Export", IconKey: "WgmExport", ToolTip: "Export this tunnel", Group: "io"),
            ])));
    }

    /// <inheritdoc />
    public void SetBusy(bool busy)
    {
        // The shell's busy overlay is driven by the presenter's operation.
    }

    /// <inheritdoc />
    public void ShowError(CoreError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        _noteText.Text = error.Message;
        _note.IsVisible = true;
    }

    private void ApplyItem(TunnelListItem item)
    {
        _heading.Text = item.ShownName;
        _stateChip.Show(item.State);
        _origin.Text = Origin(item.Source);

        _stateValue.Text = item.State;
        _sourceValue.Text = Origin(item.Source);
        _peersValue.Text = item.Peers;
        _configurationValue.Text = item.Configuration;

        if (item.Note is { } note)
        {
            _noteText.Text = note;
            _note.IsVisible = true;
        }
        else
        {
            _note.IsVisible = false;
        }
    }

    private static string Origin(TunnelSource source) =>
        source == TunnelSource.NetworkManager ? "NetworkManager" : "wg-quick configuration";

    private static Border Card(Control content)
    {
        var border = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = ThemeResources.Corner("SurfaceCornerRadius"),
            Padding = new Thickness(16),
            Child = content,
        };

        ThemeResources.Bind(border, Border.BackgroundProperty, "SurfaceBrush");
        ThemeResources.Bind(border, Border.BorderBrushProperty, "BorderBrush");
        return border;
    }

    private static StackPanel Rows(params Control[] rows)
    {
        var stack = new StackPanel();

        for (var index = 0; index < rows.Length; index++)
        {
            if (index > 0)
            {
                var hairline = new Border { Height = 1 };
                ThemeResources.Bind(hairline, Border.BackgroundProperty, "BorderBrush");
                stack.Children.Add(hairline);
            }

            stack.Children.Add(rows[index]);
        }

        return stack;
    }

    private static TextBlock Value() => new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
    };

    private static Grid DetailRow(string label, TextBlock value)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"{LabelColumn},*"),
            Margin = new Thickness(0, 10),
        };
        var caption = new TextBlock
        {
            Text = label,
            Classes = { "muted" },
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(caption, 0);
        Grid.SetColumn(value, 1);
        grid.Children.Add(caption);
        grid.Children.Add(value);
        return grid;
    }

    private static Grid RateRow(string label, string arrow, TextBlock rate, TextBlock total)
    {
        rate.Classes.Add("heading");
        rate.VerticalAlignment = VerticalAlignment.Center;
        total.Classes.Add("muted");
        total.VerticalAlignment = VerticalAlignment.Center;
        total.HorizontalAlignment = HorizontalAlignment.Right;

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"{LabelColumn},*,120"),
            Margin = new Thickness(0, 10),
        };
        var caption = new TextBlock
        {
            Text = string.Concat(arrow, " ", label),
            Classes = { "muted" },
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(caption, 0);
        Grid.SetColumn(rate, 1);
        Grid.SetColumn(total, 2);
        grid.Children.Add(caption);
        grid.Children.Add(rate);
        grid.Children.Add(total);
        return grid;
    }
}
