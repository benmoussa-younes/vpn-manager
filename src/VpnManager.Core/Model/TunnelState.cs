namespace VpnManager.Core.Model;

/// <summary>Runtime state of a tunnel as observed through the WireGuard tools.</summary>
public enum TunnelState
{
    /// <summary>No kernel interface with this name exists.</summary>
    Down = 0,

    /// <summary>The kernel interface exists and is running.</summary>
    Up = 1,

    /// <summary>The state could not be determined because the probe failed.</summary>
    Unknown = 2,
}
