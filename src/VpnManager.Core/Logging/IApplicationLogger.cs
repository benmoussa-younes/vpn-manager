namespace VpnManager.Core.Logging;

/// <summary>
/// The diagnostic log boundary. The domain and the presentation depend on this
/// contract, never on a file or a logging framework, so a host can substitute a
/// null logger or a test double.
/// </summary>
public interface IApplicationLogger
{
    /// <summary>Writes one entry.</summary>
    /// <param name="level">The severity.</param>
    /// <param name="message">The message; it must never carry a secret.</param>
    void Log(LogLevel level, string message);
}
