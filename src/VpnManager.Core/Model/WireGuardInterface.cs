namespace VpnManager.Core.Model;

/// <summary>
/// One interface as reported by <c>wg show</c>: identity, listen port and peers.
/// </summary>
public sealed record WireGuardInterface
{
    /// <summary>Gets the interface name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the interface public key, or <see langword="null"/> when unset.</summary>
    public string? PublicKey { get; init; }

    /// <summary>Gets the listening port, or <see langword="null"/> when unset.</summary>
    public int? ListenPort { get; init; }

    /// <summary>Gets the firewall mark as printed by <c>wg</c>, or <see langword="null"/>.</summary>
    public string? FwMark { get; init; }

    /// <summary>Gets the peers in the order <c>wg</c> printed them.</summary>
    public IReadOnlyList<WireGuardPeer> Peers { get; init; } = [];
}
