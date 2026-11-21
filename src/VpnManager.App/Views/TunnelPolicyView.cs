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
using VpnManager.Core.NetworkManager;
using VpnManager.Core.Presentation;

namespace VpnManager.App.Views;

/// <summary>
/// The per-profile policy page (MVP, passive view): automatic reconnection, the
/// DNS containment mode and the split-DNS routing domains. The actions live in
/// the shell toolbar; the page only reads and writes its fields.
/// </summary>
public sealed class TunnelPolicyView : UserControl, IView
{
    private const double LabelColumn = 140;

    private readonly TextBlock _tunnel;
    private readonly CheckBox _reconnect;
    private readonly ComboBox _mode;
    private readonly TextBox _search;
    private string _name = string.Empty;

    /// <summary>Initializes the policy page.</summary>
    public TunnelPolicyView()
    {
        _tunnel = new TextBlock
        {
            Classes = { "muted" },
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        _reconnect = new CheckBox { Content = "Reconnect automatically after a network change" };
        _mode = new ComboBox
        {
            ItemsSource = new[]
            {
                "Profile DNS",
                "System DNS",
                "Split DNS",
                "Full tunnel DNS",
            },
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _search = new TextBox
        {
            PlaceholderText = "corp.example, lab.example (split DNS only)",
        };

        var column = new StackPanel
        {
            Spacing = 16,
            Margin = new Thickness(24),
            Children =
            {
                Card(
                    "Policy",
                    Row("Tunnel", _tunnel),
                    Row("Reconnection", _reconnect),
                    Row("DNS mode", _mode),
                    Row("Search domains", _search)),
                new TextBlock
                {
                    Text = "Profile DNS uses the DNS servers written in the configuration. System DNS leaves the resolver untouched. Split DNS sends only the listed domains to the VPN. Full tunnel DNS sends every name to the VPN while it is up.",
                    Classes = { "muted" },
                    TextWrapping = TextWrapping.Wrap,
                },
            },
        };

        var centering = new Grid { ColumnDefinitions = new ColumnDefinitions("*,720,*") };
        Grid.SetColumn(column, 1);
        centering.Children.Add(column);

        Content = new ScrollViewer { Content = centering };
    }

    /// <summary>Raised when the operator saved the policy.</summary>
    public event Action<TunnelPolicyInput>? SaveRequested;

    /// <summary>Raised when the operator dismissed the page.</summary>
    public event Action? CancelRequested;

    /// <summary>Gets the reconnection check box, for tests.</summary>
    internal CheckBox ReconnectBox => _reconnect;

    /// <summary>Gets the DNS mode selector, for tests.</summary>
    internal ComboBox ModeBox => _mode;

    /// <summary>Gets the search-domains box, for tests.</summary>
    internal TextBox SearchBox => _search;

    /// <summary>Gets the tunnel this page edits, for tests.</summary>
    internal string TunnelName => _name;

    /// <summary>Sets the fields from the snapshot.</summary>
    /// <param name="snapshot">The policy currently stored.</param>
    public void Load(TunnelPolicySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        _name = snapshot.Name;
        _tunnel.Text = snapshot.Name;
        _reconnect.IsChecked = snapshot.AutoReconnect;
        _mode.SelectedIndex = (int)snapshot.DnsMode;
        _search.Text = string.Join(", ", snapshot.SearchDomains);
        _reconnect.Focus();
    }

    /// <summary>Reads the fields back as the policy input.</summary>
    /// <returns>The values as they are in the fields.</returns>
    public TunnelPolicyInput ReadInput() => new(
        _name,
        _reconnect.IsChecked == true,
        (NetworkManagerDnsMode)Math.Clamp(_mode.SelectedIndex, 0, 3),
        ParseDomains(_search.Text ?? string.Empty));

    /// <summary>Handles one of the page's own shell command ids.</summary>
    /// <param name="id">The command id.</param>
    /// <returns><see langword="true"/> when the id was the page's.</returns>
    public bool HandleCommand(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        switch (id)
        {
            case "policy.save":
                SaveRequested?.Invoke(ReadInput());
                return true;
            case "policy.cancel":
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
            "Policy",
            string.Concat("Reconnection and DNS for '", _name, "'."),
            new CommandSet(
            [
                new AppCommand("policy.save", "Save", IconKey: "IconSave", ToolTip: "Apply the policy", Shortcut: "Ctrl+S", Group: "primary"),
                new AppCommand("policy.cancel", "Cancel", IconKey: "IconClose", ToolTip: "Cancel", Shortcut: "Escape", Group: "dismiss"),
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

        // The presenter reports a policy failure in the shell banner.
    }

    private static string[] ParseDomains(string text) => text
        .Split([',', ';', ' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static Border Card(string heading, params Control[] rows)
    {
        var stack = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = heading, Classes = { "heading" } },
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
        AutomationProperties.SetLabeledBy(control, caption);
        Grid.SetColumn(caption, 0);
        Grid.SetColumn(control, 1);
        grid.Children.Add(caption);
        grid.Children.Add(control);
        return grid;
    }
}
