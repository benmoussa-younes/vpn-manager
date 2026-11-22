using VpnManager.Core.Configuration;
using VpnManager.Core.Model;
using VpnManager.Core.Runtime;

namespace VpnManager.Core.Tests;

/// <summary>
/// The IPv6 completeness of a profile and the host check. A profile is complete only
/// with an address and <c>::/0</c>; a partial IPv6 (or a complete one on a host with
/// no global IPv6) is stripped so no connection is left half-configured.
/// </summary>
public sealed class Ipv6ProfileTests
{
    private const string V4Only = """
        [Interface]
        Address = 10.2.0.2/32
        DNS = 10.2.0.1

        [Peer]
        PublicKey = xTIBA5rboUvnH4htodjb6e697QjLERt1NAB4mZqp8Dg=
        AllowedIPs = 0.0.0.0/0
        Endpoint = 1.2.3.4:51820
        """;

    private const string DualComplete = """
        [Interface]
        Address = 10.2.0.2/32
        Address = 2a07:b944::2:2/128
        DNS = 10.2.0.1
        DNS = 2a07:b944::2:1

        [Peer]
        PublicKey = xTIBA5rboUvnH4htodjb6e697QjLERt1NAB4mZqp8Dg=
        AllowedIPs = 0.0.0.0/0, ::/0
        Endpoint = 1.2.3.4:51820
        """;

    private const string Partial = """
        [Interface]
        Address = 10.2.0.2/32
        Address = 2a07:b944::2:2/128
        DNS = 10.2.0.1

        [Peer]
        PublicKey = xTIBA5rboUvnH4htodjb6e697QjLERt1NAB4mZqp8Dg=
        AllowedIPs = 0.0.0.0/0
        Endpoint = 1.2.3.4:51820
        """;

    [Fact]
    public void A_v4_only_profile_has_no_ipv6() =>
        Assert.Equal(Ipv6Completeness.None, Ipv6Profile.Classify(Parse(V4Only)));

    [Fact]
    public void A_dual_profile_with_an_address_and_default_route_is_complete() =>
        Assert.Equal(Ipv6Completeness.Complete, Ipv6Profile.Classify(Parse(DualComplete)));

    [Fact]
    public void An_ipv6_address_without_the_default_route_is_partial() =>
        Assert.Equal(Ipv6Completeness.Partial, Ipv6Profile.Classify(Parse(Partial)));

    [Fact]
    public void Stripping_ipv6_leaves_a_consistent_v4_profile()
    {
        var stripped = Ipv6Profile.StripIpv6(Parse(DualComplete));
        var allowed = stripped.Peers.SelectMany(peer => peer.AllowedIPs).ToArray();

        Assert.DoesNotContain(stripped.Interface.Addresses, value => value.Contains(':', StringComparison.Ordinal));
        Assert.DoesNotContain(stripped.Interface.Dns, value => value.Contains(':', StringComparison.Ordinal));
        Assert.DoesNotContain(allowed, value => value.Contains(':', StringComparison.Ordinal));
        Assert.Contains("10.2.0.2/32", stripped.Interface.Addresses);
        Assert.Contains("0.0.0.0/0", allowed);
        Assert.Equal(Ipv6Completeness.None, Ipv6Profile.Classify(stripped));
    }

    [Theory]
    [InlineData("fe800000000000000000000000000001 02 40 20 80 wlo1\n", false)]
    [InlineData("20010db8000000000000000000000001 02 40 00 00 wlo1\n", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void The_host_has_a_global_address_only_when_a_scope_zero_entry_exists(string? text, bool expected) =>
        Assert.Equal(expected, Ipv6Host.TryHasGlobalAddress(text));

    private static TunnelConfiguration Parse(string text)
    {
        var parsed = WireGuardConfigParser.Parse("wg0", text);
        Assert.True(parsed.IsSuccess, parsed.IsFailure ? parsed.Error.Message : string.Empty);
        return parsed.Value;
    }
}
