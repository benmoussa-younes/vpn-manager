namespace VpnManager.Core.Logging;

/// <summary>
/// The logger that writes nothing. It is the default for a host that has no log
/// file, and the seam tests use to assert a decision without touching a disk.
/// </summary>
public sealed class NullApplicationLogger : IApplicationLogger
{
    /// <summary>The shared instance.</summary>
    public static NullApplicationLogger Instance { get; } = new();

    /// <inheritdoc />
    public void Log(LogLevel level, string message)
    {
        // Intentionally empty: this logger discards every entry.
    }
}
