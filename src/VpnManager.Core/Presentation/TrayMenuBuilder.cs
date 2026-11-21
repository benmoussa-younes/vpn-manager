namespace VpnManager.Core.Presentation;

/// <summary>What a system-tray menu entry does.</summary>
public enum TrayMenuAction
{
    /// <summary>Show or hide the main window.</summary>
    ToggleWindow,

    /// <summary>Connect a tunnel.</summary>
    Connect,

    /// <summary>Disconnect a tunnel.</summary>
    Disconnect,

    /// <summary>Quit the application.</summary>
    Quit,
}

/// <summary>One entry of the system-tray menu.</summary>
/// <param name="Header">The label.</param>
/// <param name="Action">What it does, or <see langword="null"/> for a separator.</param>
/// <param name="TunnelName">The tunnel the entry acts on, when it acts on one.</param>
public sealed record TrayMenuItem(string Header, TrayMenuAction? Action, string? TunnelName = null)
{
    /// <summary>Gets a separator entry.</summary>
    public static TrayMenuItem Separator { get; } = new(string.Empty, null);

    /// <summary>Gets whether the entry is a separator.</summary>
    public bool IsSeparator => Action is null;
}

/// <summary>
/// Builds the system-tray menu from the tunnel list: the window toggle, up to three
/// tunnels (the connected ones first) with their connect or disconnect verb, and
/// quit. It is pure, so the menu is unit-tested without a desktop.
/// </summary>
public static class TrayMenuBuilder
{
    /// <summary>The most tunnels the tray shows.</summary>
    public const int MaximumTunnels = 3;

    /// <summary>Builds the menu.</summary>
    /// <param name="tunnels">The current tunnel list, already formatted for display.</param>
    /// <param name="windowVisible">Whether the main window is currently shown.</param>
    /// <returns>The menu entries, in order.</returns>
    public static IReadOnlyList<TrayMenuItem> Build(IReadOnlyList<TunnelListItem> tunnels, bool windowVisible)
    {
        ArgumentNullException.ThrowIfNull(tunnels);

        var items = new List<TrayMenuItem>
        {
            new(windowVisible ? "Hide window" : "Show window", TrayMenuAction.ToggleWindow),
        };

        var shown = tunnels
            .OrderByDescending(tunnel => string.Equals(tunnel.State, "Up", StringComparison.Ordinal))
            .ThenBy(tunnel => tunnel.ShownName, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumTunnels)
            .ToArray();

        if (shown.Length > 0)
        {
            items.Add(TrayMenuItem.Separator);
            foreach (var tunnel in shown)
            {
                var isUp = string.Equals(tunnel.State, "Up", StringComparison.Ordinal);
                items.Add(new TrayMenuItem(
                    isUp ? string.Concat("Disconnect ", tunnel.ShownName) : string.Concat("Connect ", tunnel.ShownName),
                    isUp ? TrayMenuAction.Disconnect : TrayMenuAction.Connect,
                    tunnel.Name));
            }
        }

        items.Add(TrayMenuItem.Separator);
        items.Add(new TrayMenuItem("Quit", TrayMenuAction.Quit));
        return items;
    }
}
