using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Core.Mvp.Composition;
using Core.Mvp.Presentation;
using Core.Mvp.Views;
using Core.Results;
using VpnManager.App.Controls;
using VpnManager.Core.Presentation;

namespace VpnManager.App.Views;

/// <summary>
/// The rename page (MVP, passive view): one text box for the display name. The
/// display name is also the NetworkManager connection id; the configuration file,
/// the interface and the real tunnel name stay unchanged. The actions live in the
/// shell toolbar.
/// </summary>
public sealed class TunnelRenameView : UserControl, IView
{
    private const double LabelColumn = 140;

    private readonly TextBlock _realName;
    private readonly TextBox _displayName;
    private string _name = string.Empty;

    /// <summary>Initializes the rename page.</summary>
    public TunnelRenameView()
    {
        _realName = new TextBlock
        {
            Classes = { "muted" },
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        _displayName = new TextBox { PlaceholderText = "Leave blank to show the real name" };

        var column = new StackPanel
        {
            Spacing = 16,
            Margin = new Thickness(24),
            Children =
            {
                Card("Display name", Row("Real name", _realName), Row("Shown as", _displayName)),
                new TextBlock
                {
                    Text = "The display name is also the NetworkManager connection name. The configuration file, the interface and the real tunnel name stay unchanged.",
                    Classes = { "muted" },
                    TextWrapping = TextWrapping.Wrap,
                },
            },
        };

        // A fixed centre column flanked by two stars keeps the max width without
        // collapsing to the content width the way a centred auto column does.
        var centering = new Grid { ColumnDefinitions = new ColumnDefinitions("*,720,*") };
        Grid.SetColumn(column, 1);
        centering.Children.Add(column);

        Content = new ScrollViewer { Content = centering };
    }

    /// <summary>Raised when the operator submitted a display name.</summary>
    public event Action<TunnelRenameInput>? SaveRequested;

    /// <summary>Raised when the operator dismissed the page.</summary>
    public event Action? CancelRequested;

    /// <summary>Gets the display-name box, for tests.</summary>
    internal TextBox DisplayNameBox => _displayName;

    /// <summary>Sets the fields from the snapshot.</summary>
    /// <param name="snapshot">The real name and the name currently shown.</param>
    public void Load(TunnelRenameSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        _name = snapshot.Name;
        _realName.Text = snapshot.Name;
        _displayName.Text = snapshot.CurrentDisplayName;
        _displayName.Focus();
        _displayName.SelectAll();
    }

    /// <summary>Handles one of the rename page's own shell command ids.</summary>
    /// <param name="id">The command id.</param>
    /// <returns><see langword="true"/> when the id was one of the page's.</returns>
    public bool HandleCommand(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        switch (id)
        {
            case "rename.save":
                SaveRequested?.Invoke(new TunnelRenameInput(_name, _displayName.Text ?? string.Empty));
                return true;
            case "rename.cancel":
                CancelRequested?.Invoke();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Publishes the page's chrome and commands to the shell.</summary>
    /// <param name="host">The shell host.</param>
    public void PublishSnapshot(IViewHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        host.SetSnapshot(new ShellSnapshot(
            "Rename tunnel",
            string.Concat("Display name for '", _name, "'; the real tunnel name never changes."),
            new CommandSet(
            [
                new AppCommand("rename.save", "Save", IconKey: "IconSave", ToolTip: "Apply the display name", Shortcut: "Ctrl+S", Group: "primary"),
                new AppCommand("rename.cancel", "Cancel", IconKey: "IconClose", ToolTip: "Cancel", Shortcut: "Escape", Group: "dismiss"),
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

        // The presenter reports a rename failure in the shell banner, so the page
        // has nothing of its own to render.
    }

    private static Border Card(string heading, params Control[] rows)
    {
        var stack = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = heading,
                    Classes = { "heading" },
                },
            },
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
        Grid.SetColumn(caption, 0);
        Grid.SetColumn(control, 1);
        grid.Children.Add(caption);
        grid.Children.Add(control);
        return grid;
    }
}
