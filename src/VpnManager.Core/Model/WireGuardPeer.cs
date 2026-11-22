using Core.Primitives;

namespace VpnManager.Core.Model;

/// <summary>
/// One peer as reported by <c>wg show &lt;interface&gt;</c>. Transfer counters
/// are kept as <see cref="ByteSize"/> so the view renders them with the
/// package's own human formatting.
/// </summary>
public sealed record WireGuardPeer
{
    /// <summary>Gets the peer public key.</summary>
    public string PublicKey { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether the peer has a preshared key.</summary>
    public bool HasPresharedKey { get; init; }

    /// <summary>Gets the endpoint as printed by <c>wg</c>, or <see langword="null"/>.</summary>
    public string? Endpoint { get; init; }

    /// <summary>Gets the allowed prefixes as printed by <c>wg</c>.</summary>
    public IReadOnlyList<string> AllowedIPs { get; init; } = [];

    /// <summary>Gets the age of the latest handshake, when <c>wg</c> reported one that parsed.</summary>
    public TimeSpan? LatestHandshakeAge { get; init; }

    /// <summary>Gets the handshake text exactly as <c>wg</c> printed it, or <see langword="null"/>.</summary>
    public string? LatestHandshakeText { get; init; }

    /// <summary>Gets the bytes received from this peer.</summary>
    public ByteSize Received { get; init; }

    /// <summary>Gets the bytes sent to this peer.</summary>
    public ByteSize Sent { get; init; }

    /// <summary>Gets the keepalive interval in seconds, or <see langword="null"/> when unset.</summary>
    public int? PersistentKeepalive { get; init; }
}
