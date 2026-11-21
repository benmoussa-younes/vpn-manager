namespace VpnManager.Core.Model;

/// <summary>
/// How a profile's IPv6 looks against the host: the profile's completeness and
/// whether the host has a global IPv6 address at all.
/// </summary>
/// <param name="Profile">The IPv6 completeness of the profile.</param>
/// <param name="HostHasIpv6">Whether the machine has a global IPv6 address.</param>
public sealed record Ipv6Description(Ipv6Completeness Profile, bool HostHasIpv6);
