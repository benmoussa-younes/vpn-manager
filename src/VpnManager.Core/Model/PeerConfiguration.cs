namespace VpnManager.Core.Model;

/// <summary>
/// The <c>[Peer]</c> section of a wg-quick configuration file.
/// </summary>
public sealed record PeerConfiguration
{
    /// <summary>Gets the peer public key.</summary>
    public string PublicKey { get; init; } = string.Empty;

    /// <summary>Gets the optional preshared key.</summary>
    public SecretKey? PresharedKey { get; init; }

    /// <summary>Gets the routed prefixes in CIDR notation, in file order.</summary>
    public IReadOnlyList<string> AllowedIPs { get; init; } = [];

    /// <summary>Gets the <c>host:port</c> endpoint, or <see langword="null"/> when unset.</summary>
    public string? Endpoint { get; init; }

    /// <summary>Gets the keepalive interval in seconds, or <see langword="null"/> when unset.</summary>
    public int? PersistentKeepalive { get; init; }

    /// <summary>
    /// Gets the operator label taken from a <c># Name = …</c> comment that
    /// precedes the peer, when one is present.
    /// </summary>
    public string? Label { get; init; }
}
