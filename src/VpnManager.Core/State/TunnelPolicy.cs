using VpnManager.Core.NetworkManager;

namespace VpnManager.Core.State;

/// <summary>
/// The per-tunnel policy the operator chose: whether NetworkManager may reconnect
/// the profile, and how its DNS entries are applied.
/// </summary>
/// <param name="AutoReconnect">Whether NetworkManager may reconnect the profile after a link change.</param>
/// <param name="DnsMode">How the profile's DNS entries are applied.</param>
/// <param name="DnsSearchDomains">The search domains routed through the tunnel in split mode.</param>
public sealed record TunnelPolicy(
    bool AutoReconnect = true,
    NetworkManagerDnsMode DnsMode = NetworkManagerDnsMode.Profile,
    IReadOnlyList<string>? DnsSearchDomains = null)
{
    /// <summary>Gets the policy a fresh tunnel starts with.</summary>
    public static TunnelPolicy Default { get; } = new();

    /// <summary>Gets the search domains, never null.</summary>
    public IReadOnlyList<string> SearchDomains => DnsSearchDomains ?? [];
}
