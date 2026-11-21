using VpnManager.Core.NetworkManager;

namespace VpnManager.Core.Presentation;

/// <summary>
/// The per-profile policy as the editor shows it: automatic reconnection, the DNS
/// containment mode and the routing domains for split DNS.
/// </summary>
/// <param name="Name">The real tunnel name.</param>
/// <param name="AutoReconnect">Whether NetworkManager may bring the profile back.</param>
/// <param name="DnsMode">How the profile's DNS is applied.</param>
/// <param name="SearchDomains">The routing domains that go to the VPN DNS.</param>
public sealed record TunnelPolicySnapshot(
    string Name,
    bool AutoReconnect,
    NetworkManagerDnsMode DnsMode,
    IReadOnlyList<string> SearchDomains);

/// <summary>The values the policy editor read back when the operator saved.</summary>
/// <param name="Name">The real tunnel name.</param>
/// <param name="AutoReconnect">Whether NetworkManager may bring the profile back.</param>
/// <param name="DnsMode">How the profile's DNS is applied.</param>
/// <param name="SearchDomains">The routing domains that go to the VPN DNS.</param>
public sealed record TunnelPolicyInput(
    string Name,
    bool AutoReconnect,
    NetworkManagerDnsMode DnsMode,
    IReadOnlyList<string> SearchDomains);
