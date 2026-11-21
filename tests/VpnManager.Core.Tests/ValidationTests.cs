using VpnManager.Core.Validation;

namespace VpnManager.Core.Tests;

/// <summary>The primitive text rules, each with a boundary and a refusal.</summary>
public sealed class WireGuardKeyTests
{
    [Fact]
    public void A_canonical_32_byte_key_is_valid() => Assert.True(WireGuardKey.IsValid(TestData.InterfacePrivateKey));

    [Fact]
    public void A_derived_public_key_of_the_same_shape_is_valid() => Assert.True(WireGuardKey.IsValid(TestData.DerivedPublicKey));

    [Theory]
    [InlineData("yAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBmk")]   // 43 characters
    [InlineData("yAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBmk==")] // 45 characters
    [InlineData("yAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBm==")]  // 44 characters decoding to 31 bytes
    [InlineData("yAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBmk!")] // illegal base64 character
    [InlineData("yAnz5TF+lXXJte14tji3zlM q+hd2rYUIgJBgB3fBmk=")] // embedded whitespace
    [InlineData("")]
    public void A_malformed_key_is_refused(string candidate) => Assert.False(WireGuardKey.IsValid(candidate));

    [Fact]
    public void A_null_key_is_refused() => Assert.False(WireGuardKey.IsValid(null));

    [Fact]
    public void The_boundaries_of_the_shape_are_the_documented_ones()
    {
        Assert.Equal(32, WireGuardKey.KeyBytes);
        Assert.Equal(44, WireGuardKey.EncodedLength);
        Assert.Equal(WireGuardKey.EncodedLength, TestData.InterfacePrivateKey.Length);
    }
}

/// <summary>The tunnel name is both a file stem and a kernel interface name.</summary>
public sealed class TunnelNameRulesTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("wg0")]
    [InlineData("abcdefghijklmno")] // exactly 15
    [InlineData("wg-0_x+y=z.a")]
    public void A_usable_name_is_accepted(string name) => Assert.True(TunnelNameRules.IsValid(name));

    [Theory]
    [InlineData("abcdefghijklmnop")] // 16 characters
    [InlineData("wg/0")]
    [InlineData("wg 0")]
    [InlineData("wg0\n")]
    [InlineData("wg0:1")]
    [InlineData("-x")]
    [InlineData("")]
    public void An_unusable_name_is_refused(string name) => Assert.False(TunnelNameRules.IsValid(name));

    [Fact]
    public void A_null_name_is_refused() => Assert.False(TunnelNameRules.IsValid(null));

    [Fact]
    public void The_boundary_is_IFNAMSIZ_minus_one()
    {
        Assert.Equal(15, TunnelNameRules.MaximumLength);
        Assert.True(TunnelNameRules.IsValid(new string('a', TunnelNameRules.MaximumLength)));
        Assert.False(TunnelNameRules.IsValid(new string('a', TunnelNameRules.MaximumLength + 1)));
    }
}

/// <summary>Address, endpoint and resolver text.</summary>
public sealed class NetworkTextTests
{
    [Theory]
    [InlineData("10.0.0.2/24")]
    [InlineData("10.0.0.2")]
    [InlineData("0.0.0.0/0")]
    [InlineData("255.255.255.255/32")]
    [InlineData("2001:db8::2/64")]
    [InlineData("::/0")]
    [InlineData("2001:db8::2")]
    [InlineData("2001:db8::/128")]
    public void An_address_with_a_valid_prefix_is_accepted(string value) =>
        Assert.True(NetworkText.IsValidAddressOrPrefix(value));

    [Theory]
    [InlineData("10.0.0.0/33")]
    [InlineData("2001:db8::/129")]
    [InlineData("10.0.0.0/abc")]
    [InlineData("10.0.0.0/-1")]
    [InlineData("not.an.address")]
    [InlineData("300.0.0.0/8")]
    [InlineData("10.0.0.0/")]
    [InlineData("")]
    public void An_address_outside_the_prefix_range_is_refused(string value) =>
        Assert.False(NetworkText.IsValidAddressOrPrefix(value));

    [Fact]
    public void A_null_address_is_refused() => Assert.False(NetworkText.IsValidAddressOrPrefix(null));

    [Theory]
    [InlineData("vpn.example.com:51820")]
    [InlineData("1.2.3.4:51820")]
    [InlineData("1.2.3.4:1")]      // boundary: smallest usable port
    [InlineData("1.2.3.4:65535")]  // boundary: largest usable port
    [InlineData("[2001:db8::1]:51820")]
    public void A_usable_endpoint_is_accepted(string value) => Assert.True(NetworkText.IsValidEndpoint(value));

    [Theory]
    [InlineData("1.2.3.4")]
    [InlineData("1.2.3.4:0")]
    [InlineData("1.2.3.4:65536")]
    [InlineData("1.2.3.4:abc")]
    [InlineData(":51820")]
    [InlineData("1.2.3.4:")]
    [InlineData("2001:db8::1:51820")] // IPv6 must be bracketed
    [InlineData("vpn example com:51820")]
    [InlineData("")]
    public void An_endpoint_that_is_not_host_colon_port_is_refused(string value) =>
        Assert.False(NetworkText.IsValidEndpoint(value));

    [Fact]
    public void A_null_endpoint_is_refused() => Assert.False(NetworkText.IsValidEndpoint(null));

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("dns.example.com")]
    [InlineData("internal-dns")]
    public void A_usable_resolver_entry_is_accepted(string value) => Assert.True(NetworkText.IsValidDnsEntry(value));

    [Theory]
    [InlineData("not a host!")]
    [InlineData("dns example com")]
    [InlineData("")]
    public void A_resolver_entry_that_is_neither_address_nor_host_is_refused(string value) =>
        Assert.False(NetworkText.IsValidDnsEntry(value));
}
