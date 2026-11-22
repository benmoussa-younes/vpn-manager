using Core.Results;
using VpnManager.Core.Model;

namespace VpnManager.Core.Validation;

/// <summary>
/// The single validation entry point for a tunnel configuration. It refuses the
/// first rule that fails with a specific code and an actionable message, so the
/// editor can point at the offending field without guessing.
/// </summary>
/// <remarks>
/// A message never contains key material: only the public key may be quoted
/// back to the operator.
/// </remarks>
public static class TunnelValidator
{
    /// <summary>The smallest MTU the application accepts.</summary>
    public const int MinimumMtu = 576;

    /// <summary>The largest MTU the application accepts.</summary>
    public const int MaximumMtu = 65535;

    /// <summary>
    /// Validates the whole configuration, returning the same instance on success.
    /// </summary>
    /// <param name="configuration">The configuration to check.</param>
    /// <returns>The configuration, or the first failure.</returns>
    public static Result<TunnelConfiguration> Validate(TunnelConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var nameFailure = ValidateName(configuration.Name);
        if (nameFailure is not null)
        {
            return Result<TunnelConfiguration>.Failure(nameFailure);
        }

        var interfaceFailure = ValidateInterface(configuration.Interface);
        if (interfaceFailure is not null)
        {
            return Result<TunnelConfiguration>.Failure(interfaceFailure);
        }

        var seenPublicKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var peer in configuration.Peers)
        {
            var peerFailure = ValidatePeer(peer, seenPublicKeys);
            if (peerFailure is not null)
            {
                return Result<TunnelConfiguration>.Failure(peerFailure);
            }
        }

        return Result<TunnelConfiguration>.Success(configuration);
    }

    private static CoreError? ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return WireGuardErrors.Validation(
                WireGuardErrorCodes.TunnelNameEmpty,
                "Enter a tunnel name; it becomes the interface name and the configuration file stem.");
        }

        if (!TunnelNameRules.IsValid(name))
        {
            return WireGuardErrors.Validation(
                WireGuardErrorCodes.TunnelNameInvalid,
                $"'{name}' is not a usable interface name: use 1 to {TunnelNameRules.MaximumLength} characters from A-Z, a-z, 0-9, '_', '=', '+', '.' and '-'.");
        }

        return null;
    }

    private static CoreError? ValidateInterface(InterfaceSettings settings)
    {
        if (settings.PrivateKey.IsEmpty)
        {
            return WireGuardErrors.Validation(
                WireGuardErrorCodes.PrivateKeyMissing,
                "Provide the interface PrivateKey, or generate a new key pair.");
        }

        if (!WireGuardKey.IsValid(settings.PrivateKey.Reveal()))
        {
            return WireGuardErrors.Validation(
                WireGuardErrorCodes.PrivateKeyInvalid,
                "The interface PrivateKey is not a 32-byte base64 WireGuard key.");
        }

        if (settings.Addresses.Count == 0)
        {
            return WireGuardErrors.Validation(
                WireGuardErrorCodes.AddressMissing,
                "Add at least one Address in address/prefix form, for example 10.10.0.2/24.");
        }

        foreach (var address in settings.Addresses)
        {
            if (!NetworkText.IsValidAddressOrPrefix(address))
            {
                return WireGuardErrors.Validation(
                    WireGuardErrorCodes.AddressInvalid,
                    $"Address '{address}' is not an address with a valid prefix, for example 10.10.0.2/24.");
            }
        }

        foreach (var dns in settings.Dns)
        {
            if (!NetworkText.IsValidDnsEntry(dns))
            {
                return WireGuardErrors.Validation(
                    WireGuardErrorCodes.DnsInvalid,
                    $"DNS entry '{dns}' is neither an IP address, a host name, nor a ~routing-domain.");
            }
        }

        if (settings.Table is { Length: > 0 } table && !NetworkText.IsValidTableValue(table))
        {
            return WireGuardErrors.Validation(
                WireGuardErrorCodes.TableInvalid,
                $"Table '{table}' is not 'auto', 'off', or a route table number.");
        }

        if (settings.FwMark is { Length: > 0 } fwMark && !NetworkText.IsValidFwMark(fwMark))
        {
            return WireGuardErrors.Validation(
                WireGuardErrorCodes.FwMarkInvalid,
                "FwMark must be 'off', a 32-bit number, or hexadecimal such as 0xca6c.");
        }

        if (settings.ListenPort is { } listenPort && listenPort is < 0 or > 65535)
        {
            return WireGuardErrors.Validation(
                WireGuardErrorCodes.ListenPortInvalid,
                $"ListenPort {listenPort} is outside 0 to 65535; 0 lets the kernel choose a port.");
        }

        if (settings.Mtu is { } mtu && mtu is < MinimumMtu or > MaximumMtu)
        {
            return WireGuardErrors.Validation(
                WireGuardErrorCodes.MtuInvalid,
                $"MTU {mtu} is outside {MinimumMtu} to {MaximumMtu}; a WireGuard tunnel typically uses 1420.");
        }

        return null;
    }

    private static CoreError? ValidatePeer(PeerConfiguration peer, HashSet<string> seenPublicKeys)
    {
        if (string.IsNullOrWhiteSpace(peer.PublicKey))
        {
            return WireGuardErrors.Validation(
                WireGuardErrorCodes.PeerPublicKeyMissing,
                "Every peer needs a PublicKey: paste the public key the other side generated.");
        }

        if (!WireGuardKey.IsValid(peer.PublicKey))
        {
            return WireGuardErrors.Validation(
                WireGuardErrorCodes.KeyInvalid,
                "A peer PublicKey is not a 32-byte base64 WireGuard key.");
        }

        if (!seenPublicKeys.Add(peer.PublicKey))
        {
            return WireGuardErrors.Validation(
                WireGuardErrorCodes.PeerPublicKeyDuplicate,
                $"Peer '{peer.PublicKey}' is listed more than once; each peer may appear once.");
        }

        if (peer.PresharedKey is { IsEmpty: false } presharedKey && !WireGuardKey.IsValid(presharedKey.Reveal()))
        {
            return WireGuardErrors.Validation(
                WireGuardErrorCodes.PresharedKeyInvalid,
                "A peer PresharedKey is not a 32-byte base64 WireGuard key.");
        }

        if (peer.AllowedIPs.Count == 0)
        {
            return WireGuardErrors.Validation(
                WireGuardErrorCodes.PeerAllowedIpsMissing,
                "Every peer needs at least one AllowedIPs entry, for example 0.0.0.0/0 to route everything through the peer.");
        }

        foreach (var allowed in peer.AllowedIPs)
        {
            if (!NetworkText.IsValidAddressOrPrefix(allowed))
            {
                return WireGuardErrors.Validation(
                    WireGuardErrorCodes.AllowedIpsInvalid,
                    $"AllowedIPs entry '{allowed}' is not an address with a valid prefix.");
            }
        }

        if (peer.Endpoint is { Length: > 0 } endpoint && !NetworkText.IsValidEndpoint(endpoint))
        {
            return WireGuardErrors.Validation(
                WireGuardErrorCodes.EndpointInvalid,
                $"Endpoint '{endpoint}' must be host:port, for example vpn.example.com:51820, or [2001:db8::1]:51820.");
        }

        if (peer.PersistentKeepalive is { } keepalive && keepalive is < 0 or > 65535)
        {
            return WireGuardErrors.Validation(
                WireGuardErrorCodes.KeepaliveInvalid,
                $"PersistentKeepalive {keepalive} is outside 0 to 65535 seconds; 25 keeps a NAT mapping open.");
        }

        return null;
    }
}
