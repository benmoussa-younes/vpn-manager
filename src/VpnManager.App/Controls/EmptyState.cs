using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace VpnManager.App.Controls;

/// <summary>
/// A centred placeholder for an empty list or table: a heading that says what is
/// missing and a muted detail that says what to do about it.
/// </summary>
public sealed class EmptyState : UserControl
{
    private readonly TextBlock _heading;
    private readonly TextBlock _detail;
    private readonly Button _action;

    /// <summary>Initializes the placeholder.</summary>
    public EmptyState()
    {
        _heading = new TextBlock
        {
            Classes = { "heading" },
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        _detail = new TextBlock
        {
            Classes = { "muted" },
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        };

        _action = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0),
            IsVisible = false,
        };
        _action.Click += (_, _) => ActionRequested?.Invoke();

        Content = new StackPanel
        {
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _heading, _detail, _action },
        };
    }

    /// <summary>Raised when the operator activated the optional action.</summary>
    public event Action? ActionRequested;

    /// <summary>Sets the two lines of the placeholder and an optional action.</summary>
    /// <param name="heading">What is missing.</param>
    /// <param name="detail">What the operator can do about it.</param>
    /// <param name="actionLabel">The action button label, or empty for none.</param>
    public void Configure(string heading, string detail, string actionLabel = "")
    {
        ArgumentNullException.ThrowIfNull(heading);
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentNullException.ThrowIfNull(actionLabel);

        _heading.Text = heading;
        _detail.Text = detail;
        _action.Content = actionLabel;
        _action.IsVisible = actionLabel.Length > 0;
    }

    /// <summary>Gets the heading line.</summary>
    public string Heading => _heading.Text ?? string.Empty;

    /// <summary>Gets the detail line.</summary>
    public string Detail => _detail.Text ?? string.Empty;

    /// <summary>Gets the action button, for tests.</summary>
    internal Button ActionButton => _action;

    /// <summary>Gets the action label, or an empty string.</summary>
    public string ActionLabel => _action.IsVisible ? _action.Content?.ToString() ?? string.Empty : string.Empty;
}
