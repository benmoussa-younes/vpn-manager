using VpnManager.Core.Runtime;

namespace VpnManager.Core.Tests;

/// <summary>
/// Parsing <c>wg show</c> text: the real shape, the boundaries of the counters
/// and the handshake clock, and a refusal for structurally broken text.
/// </summary>
public sealed class WireGuardShowParserTests
{
    [Fact]
    public void The_real_interface_shape_parses()
    {
        var parsed = WireGuardShowParser.ParseInterfaces(TestData.SampleShowText);

        Assert.True(parsed.IsSuccess, parsed.IsFailure ? parsed.Error.Message : string.Empty);
        var interfaceValue = Assert.Single(parsed.Value);

        Assert.Equal("wg0", interfaceValue.Name);
        Assert.Equal(TestData.PeerOneKey, interfaceValue.PublicKey);
        Assert.Equal(51820, interfaceValue.ListenPort);
        Assert.Null(interfaceValue.FwMark);
        Assert.Equal(2, interfaceValue.Peers.Count);
    }

    [Fact]
    public void The_first_peer_parses_every_field()
    {
        var peer = WireGuardShowParser.ParseInterfaces(TestData.SampleShowText).Value[0].Peers[0];

        Assert.Equal(TestData.PeerTwoKey, peer.PublicKey);
        Assert.True(peer.HasPresharedKey);
        Assert.Equal("vpn.example.com:51820", peer.Endpoint);
        Assert.Equal(["10.10.0.0/24", "0.0.0.0/0"], peer.AllowedIPs);
        Assert.Equal("1 minute, 20 seconds ago", peer.LatestHandshakeText);
        Assert.Equal(TimeSpan.FromSeconds(80), peer.LatestHandshakeAge);
        Assert.Equal(1239, peer.Received.Bytes);
        Assert.Equal(3670016, peer.Sent.Bytes);
        Assert.Equal(25, peer.PersistentKeepalive);
    }

    [Fact]
    public void The_second_peer_parses_its_boundaries()
    {
        var peer = WireGuardShowParser.ParseInterfaces(TestData.SampleShowText).Value[0].Peers[1];

        Assert.False(peer.HasPresharedKey);
        Assert.Equal("203.0.113.9:51820", peer.Endpoint);
        Assert.Equal(["10.10.0.0/24"], peer.AllowedIPs);
        Assert.Equal("Now", peer.LatestHandshakeText);
        Assert.Equal(TimeSpan.Zero, peer.LatestHandshakeAge);
        Assert.Equal(0, peer.Received.Bytes);
        Assert.Equal(0, peer.Sent.Bytes);
        Assert.Null(peer.PersistentKeepalive);
    }

    [Fact]
    public void A_long_handshake_is_summed_exactly()
    {
        var peer = ParseSinglePeer("latest handshake: 2 days, 3 hours, 12 minutes, 4 seconds ago");

        Assert.Equal(new TimeSpan(2, 3, 12, 4), peer.LatestHandshakeAge);
    }

    [Fact]
    public void An_unrecognised_handshake_keeps_the_text_and_reports_no_age()
    {
        var peer = ParseSinglePeer("latest handshake: 3 fortnights ago");

        Assert.Null(peer.LatestHandshakeAge);
        Assert.Equal("3 fortnights ago", peer.LatestHandshakeText);
    }

    [Fact]
    public void A_large_transfer_in_tebibytes_parses()
    {
        var peer = ParseSinglePeer("transfer: 1.00 TiB received, 0.50 TiB sent");

        Assert.Equal(1099511627776, peer.Received.Bytes);
        Assert.Equal(549755813888, peer.Sent.Bytes);
    }

    [Theory]
    [InlineData("transfer: 1.00 B received, 1.00 B sent", 1)]
    [InlineData("transfer: 1.00 KiB received, 1.00 KiB sent", 1024)]
    [InlineData("transfer: 1.00 MiB received, 1.00 MiB sent", 1048576)]
    [InlineData("transfer: 1.00 GiB received, 1.00 GiB sent", 1073741824)]
    public void Every_transfer_unit_parses(string peerLine, long expected)
    {
        var peer = ParseSinglePeer(peerLine);

        Assert.Equal(expected, peer.Received.Bytes);
        Assert.Equal(expected, peer.Sent.Bytes);
    }

    [Fact]
    public void An_unrecognised_transfer_leaves_the_counters_at_zero()
    {
        var peer = ParseSinglePeer("transfer: lots received, more sent");

        Assert.Equal(0, peer.Received.Bytes);
        Assert.Equal(0, peer.Sent.Bytes);
    }

    [Fact]
    public void An_hidden_preshared_key_is_reported_as_present_and_never_as_a_value()
    {
        const string text = """
            interface: wg0
              public key: xTIBA5rboUvnH4htodjb6e697QjLERt1NAB4mZqp8Dg=

            peer: AAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBmk=
              preshared key: (hidden)
              allowed ips: 10.10.0.0/24
            """;

        var parsed = WireGuardShowParser.ParseInterfaces(text);

        Assert.True(parsed.IsSuccess);
        var peer = parsed.Value[0].Peers[0];
        Assert.True(peer.HasPresharedKey);
        Assert.DoesNotContain("hidden", peer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_preshared_key_is_reported_as_absent()
    {
        var peer = ParseSinglePeer("preshared key: (none)");

        Assert.False(peer.HasPresharedKey);
    }

    [Fact]
    public void A_firewall_mark_is_read_when_set()
    {
        const string text = """
            interface: wg0
              public key: xTIBA5rboUvnH4htodjb6e697QjLERt1NAB4mZqp8Dg=
              fwmark: 0x1234
            """;

        var parsed = WireGuardShowParser.ParseInterfaces(text);

        Assert.True(parsed.IsSuccess);
        Assert.Equal("0x1234", parsed.Value[0].FwMark);
    }

    [Fact]
    public void Two_interfaces_in_one_output_become_two_values()
    {
        const string text = """
            interface: wg0
              public key: xTIBA5rboUvnH4htodjb6e697QjLERt1NAB4mZqp8Dg=
              listening port: 51820

            interface: wg1
              public key: AAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBmk=
              listening port: 51821
            """;

        var parsed = WireGuardShowParser.ParseInterfaces(text);

        Assert.True(parsed.IsSuccess);
        Assert.Equal(2, parsed.Value.Count);
        Assert.Equal("wg0", parsed.Value[0].Name);
        Assert.Equal("wg1", parsed.Value[1].Name);
        Assert.Equal(51821, parsed.Value[1].ListenPort);
    }

    [Fact]
    public void An_unknown_property_is_ignored_so_a_newer_wg_still_parses()
    {
        var peer = ParseSinglePeer("future property: something");

        Assert.Equal(TestData.PeerTwoKey, peer.PublicKey);
    }

    [Fact]
    public void Empty_output_is_an_empty_list_not_a_failure()
    {
        Assert.True(WireGuardShowParser.ParseInterfaces(null).IsSuccess);
        Assert.Empty(WireGuardShowParser.ParseInterfaces(string.Empty).Value);
        Assert.Empty(WireGuardShowParser.ParseInterfaces("   \n\n").Value);
    }

    [Fact]
    public void A_property_before_any_interface_is_refused()
    {
        var parsed = WireGuardShowParser.ParseInterfaces("  public key: xTIBA5rboUvnH4htodjb6e697QjLERt1NAB4mZqp8Dg=");

        Assert.True(parsed.IsFailure);
        Assert.Equal(VpnManager.Core.Model.WireGuardErrorCodes.ParseError, parsed.Error.Code);
        Assert.Contains("Line 1", parsed.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_peer_before_any_interface_is_refused()
    {
        var parsed = WireGuardShowParser.ParseInterfaces("peer: xTIBA5rboUvnH4htodjb6e697QjLERt1NAB4mZqp8Dg=");

        Assert.True(parsed.IsFailure);
        Assert.Contains("Line 1", parsed.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_line_that_is_not_key_colon_value_is_refused()
    {
        var parsed = WireGuardShowParser.ParseInterfaces("interface: wg0\nthis line has no colon");

        Assert.True(parsed.IsFailure);
        Assert.Equal(VpnManager.Core.Model.WireGuardErrorCodes.ParseError, parsed.Error.Code);
    }

    [Fact]
    public void Interface_names_are_split_on_whitespace()
    {
        var parsed = WireGuardShowParser.ParseInterfaceNames("wg0 wg1\twg2\n");

        Assert.True(parsed.IsSuccess);
        Assert.Equal(["wg0", "wg1", "wg2"], parsed.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n")]
    public void No_interfaces_parses_as_an_empty_name_list(string? output)
    {
        var parsed = WireGuardShowParser.ParseInterfaceNames(output);

        Assert.True(parsed.IsSuccess);
        Assert.Empty(parsed.Value);
    }

    private static VpnManager.Core.Model.WireGuardPeer ParseSinglePeer(string peerLine)
    {
        var text = $"""
            interface: wg0
              public key: xTIBA5rboUvnH4htodjb6e697QjLERt1NAB4mZqp8Dg=

            peer: AAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBmk=
              allowed ips: 10.10.0.0/24
              {peerLine}
            """;

        var parsed = WireGuardShowParser.ParseInterfaces(text);
        Assert.True(parsed.IsSuccess, parsed.IsFailure ? parsed.Error.Message : string.Empty);
        return parsed.Value[0].Peers[0];
    }
}
