using VpnManager.Core.Model;

namespace VpnManager.Core.Configuration;

/// <summary>
/// Reads how complete a profile's IPv6 is and can strip it. It is pure, so the
/// decision is tested without a machine.
/// </summary>
/// <remarks>
/// A profile is <see cref="Ipv6Completeness.Complete"/> only when it has an IPv6
/// address <em>and</em> routes <c>::/0</c>; anything else that mentions IPv6 is
/// partial and would be half-configured on connect.
/// </remarks>
public static class Ipv6Profile
{
    /// <summary>Classifies the profile's IPv6.</summary>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The completeness.</returns>
    public static Ipv6Completeness Classify(TunnelConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var hasAddress = configuration.Interface.Addresses.Any(IsIpv6);
        var routes = AllowedIPs(configuration).ToArray();
        var hasRoute = routes.Any(IsIpv6);

        if (!hasAddress && !configuration.Interface.Dns.Any(IsIpv6) && !hasRoute)
        {
            return Ipv6Completeness.None;
        }

        return hasAddress && routes.Any(IsIpv6Default)
            ? Ipv6Completeness.Complete
            : Ipv6Completeness.Partial;
    }

    /// <summary>Returns a copy with every IPv6 address, resolver and prefix removed.</summary>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The IPv4-only configuration.</returns>
    public static TunnelConfiguration StripIpv6(TunnelConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var settings = configuration.Interface with
        {
            Addresses = configuration.Interface.Addresses.Where(address => !IsIpv6(address)).ToArray(),
            Dns = configuration.Interface.Dns.Where(dns => !IsIpv6(dns)).ToArray(),
        };

        var peers = configuration.Peers
            .Select(peer => peer with { AllowedIPs = peer.AllowedIPs.Where(prefix => !IsIpv6(prefix)).ToArray() })
            .ToArray();

        return configuration with { Interface = settings, Peers = peers };
    }

    private static IEnumerable<string> AllowedIPs(TunnelConfiguration configuration) =>
        configuration.Peers.SelectMany(peer => peer.AllowedIPs);

    private static bool IsIpv6(string value) => value.Contains(':', StringComparison.Ordinal);

    private static bool IsIpv6Default(string value) => value.StartsWith("::/0", StringComparison.Ordinal);
}
