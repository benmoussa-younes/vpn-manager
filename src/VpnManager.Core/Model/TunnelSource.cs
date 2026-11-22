namespace VpnManager.Core.Model;

/// <summary>
/// What owns a tunnel. A configuration file is the historical model; a
/// NetworkManager connection is the one the workstation product provisions, so
/// the application must show which is which.
/// </summary>
public enum TunnelSource
{
    /// <summary>A <c>wg-quick</c> configuration file.</summary>
    Configuration,

    /// <summary>A NetworkManager WireGuard connection.</summary>
    NetworkManager,
}
