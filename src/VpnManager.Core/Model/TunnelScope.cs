namespace VpnManager.Core.Model;

/// <summary>
/// Which configuration directory a tunnel belongs to. The application only
/// manages the user scope; the system value is retained for the state document,
/// so an entry written by an earlier version stays readable.
/// </summary>
public enum TunnelScope
{
    /// <summary>The machine-wide scope; the per-user application never manages it.</summary>
    System,

    /// <summary>A per-user directory, managed without elevation.</summary>
    User,
}
