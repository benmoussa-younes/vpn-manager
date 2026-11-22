namespace VpnManager.Core.Model;

/// <summary>
/// How a tunnel routes. It mirrors the workstation overlay's two modes: a split
/// tunnel carries only the mesh, a full tunnel carries the default route and the
/// LAN through the peer (the operator decision of 2026-09-21 on `ws02`).
/// </summary>
public enum TunnelRoutingMode
{
    /// <summary>Only the mesh is routed through the tunnel; the Internet stays direct.</summary>
    Split = 0,

    /// <summary>The default route and the LAN go through the peer, with a fwmark to keep the transport out.</summary>
    Full = 1,
}
