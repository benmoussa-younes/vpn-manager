using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace VpnManager.App.Controls;

/// <summary>
/// Applies the shared <c>Core.UI</c> theme to the controls this
/// application builds in code.
/// </summary>
internal static class ThemeResources
{
    /// <summary>
    /// Binds a property to a theme token as a dynamic resource, so a control built
    /// in code follows a live light/dark switch instead of keeping the brush it was
    /// created with.
    /// </summary>
    /// <param name="target">The control to bind.</param>
    /// <param name="property">The property to bind.</param>
    /// <param name="key">The token key.</param>
    internal static void Bind(AvaloniaObject target, AvaloniaProperty property, string key)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);

        target.Bind(property, new DynamicResourceExtension(key));
    }

    /// <summary>Gets a corner-radius token, or a square corner when the key is absent.</summary>
    /// <param name="key">One of the shared corner-radius keys (<c>ControlCornerRadius</c>, <c>SurfaceCornerRadius</c>).</param>
    /// <returns>The corner radius.</returns>
    internal static CornerRadius Corner(string key) =>
        Application.Current is { } application && application.TryFindResource(key, out var value) && value is CornerRadius radius
            ? radius
            : default;
}
