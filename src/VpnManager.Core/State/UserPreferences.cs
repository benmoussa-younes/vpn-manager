namespace VpnManager.Core.State;

/// <summary>
/// The operator's appearance preferences. It holds no secret; it is stored only
/// when the operator asks to remember it, under the XDG state directory.
/// </summary>
public sealed record UserPreferences
{
    /// <summary>Gets the default preferences: follow the system, the studio accent.</summary>
    public static UserPreferences Default { get; } = new();

    /// <summary>Gets the appearance variant.</summary>
    public ThemePreference Theme { get; init; } = ThemePreference.System;

    /// <summary>Gets the accent palette key, or <see langword="null"/> for the default accent.</summary>
    public string? AccentKey { get; init; }
}
