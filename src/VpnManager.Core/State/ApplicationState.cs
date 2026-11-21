using VpnManager.Core.Model;

namespace VpnManager.Core.State;

/// <summary>
/// The application's own state: what it knows about the tunnels and the last scope
/// it used. It is saved by the application, owned by the operator, and never holds
/// a private key.
/// </summary>
public sealed record ApplicationState
{
    /// <summary>The current schema version; bump it when the shape changes.</summary>
    public const int CurrentVersion = 2;

    private Dictionary<string, string> _displayNames = new(StringComparer.Ordinal);

    /// <summary>Gets the schema version of the saved state.</summary>
    public int Version { get; init; } = CurrentVersion;

    /// <summary>Gets the scope the operator last worked in, or <see langword="null"/>.</summary>
    public TunnelScope? LastScope { get; init; }

    /// <summary>Gets the known tunnels.</summary>
    public IReadOnlyList<TunnelStateEntry> Tunnels { get; init; } = [];

    /// <summary>
    /// Gets the operator's display names, keyed by the real tunnel name. The name
    /// is only what the application shows; the file, the interface and the
    /// NetworkManager connection keep the real name. A missing entry shows the real
    /// name. It is never null, even when a saved document has no such property.
    /// </summary>
    public Dictionary<string, string> DisplayNames
    {
        get => _displayNames;
        init => _displayNames = value ?? new(StringComparer.Ordinal);
    }
}
