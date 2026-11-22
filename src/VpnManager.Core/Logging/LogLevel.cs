namespace VpnManager.Core.Logging;

/// <summary>The severity of a log entry, from least to most urgent.</summary>
public enum LogLevel
{
    /// <summary>Development detail.</summary>
    Debug = 0,

    /// <summary>Normal lifecycle events.</summary>
    Information = 1,

    /// <summary>Something recoverable went wrong.</summary>
    Warning = 2,

    /// <summary>Something failed.</summary>
    Error = 3,
}
