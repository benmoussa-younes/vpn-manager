using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;

namespace VpnManager.App.Controls;

/// <summary>
/// A small state token: a coloured dot and the state word. It replaces the plain
/// bracketed state text a row used to show, and follows the same colour language as
/// the rest of the window (success, muted, warning).
/// </summary>
public sealed class StatusChip : UserControl
{
    private readonly Ellipse _dot;
    private readonly TextBlock _text;

    /// <summary>Initializes the chip in its cleared state.</summary>
    public StatusChip()
    {
        _dot = new Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center };
        _text = new TextBlock { VerticalAlignment = VerticalAlignment.Center };

        Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { _dot, _text },
        };

        Clear();
    }

    /// <summary>Gets the state the chip currently shows.</summary>
    public string State { get; private set; } = string.Empty;

    /// <summary>Shows a state word; an empty word hides the chip.</summary>
    /// <param name="state">One of <c>Up</c>, <c>Down</c>, <c>Unknown</c>, or empty.</param>
    public void Show(string state)
    {
        ArgumentNullException.ThrowIfNull(state);

        State = state;
        _text.Text = state;

        // A dynamic resource, so the dot follows a live light/dark switch.
        ThemeResources.Bind(_dot, Shape.FillProperty, state switch
        {
            "Up" => "SuccessBrush",
            "Unknown" => "WarningBrush",
            _ => "MutedForegroundBrush",
        });

        IsVisible = state.Length > 0;
    }

    /// <summary>Hides the chip and drops its state.</summary>
    public void Clear()
    {
        State = string.Empty;
        _text.Text = string.Empty;
        IsVisible = false;
    }
}
