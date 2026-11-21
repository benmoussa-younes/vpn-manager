namespace VpnManager.App;

/// <summary>
/// Reads the launch options that decide what the application manages. It is split
/// out of the entry point so the parsing is unit-tested without starting the GUI.
/// </summary>
internal static class LaunchOptions
{
    /// <summary>The flag that overrides the per-user configuration directory.</summary>
    internal const string ConfigDirectoryFlag = "--config-dir";

    /// <summary>The flag that disables every action that would change a tunnel.</summary>
    internal const string ReadOnlyFlag = "--read-only";

    /// <summary>Reads whether the session must be read-only.</summary>
    /// <param name="arguments">The raw process arguments.</param>
    /// <returns><see langword="true"/> when the flag is present.</returns>
    internal static bool IsReadOnly(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return arguments.Contains(ReadOnlyFlag, StringComparer.Ordinal);
    }

    /// <summary>Reads a two-token option value, or <see langword="null"/> when it is absent.</summary>
    /// <param name="arguments">The raw process arguments.</param>
    /// <param name="name">The option name.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    internal static string? ReadValue(string[] arguments, string name)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var index = Array.IndexOf(arguments, name);
        return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
    }
}
