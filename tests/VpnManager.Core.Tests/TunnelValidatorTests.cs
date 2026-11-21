using Core.Results;
using VpnManager.Core.Model;
using VpnManager.Core.Validation;

namespace VpnManager.Core.Tests;

/// <summary>
/// One test per validation rule, each refusing with a specific code and an
/// actionable message. A message must never quote key material.
/// </summary>
public sealed class TunnelValidatorTests
{
    [Fact]
    public void A_complete_configuration_passes_and_is_returned_unchanged()
    {
        var configuration = TestData.ValidConfiguration();

        var result = TunnelValidator.Validate(configuration);

        Assert.True(result.IsSuccess);
        Assert.Same(configuration, result.Value);
    }

    [Fact]
    public void An_interface_without_peers_is_allowed()
    {
        var result = TunnelValidator.Validate(TestData.ValidConfiguration() with { Peers = [] });

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_name_is_refused(string name) =>
        Assert.Equal(WireGuardErrorCodes.TunnelNameEmpty, Refuse(configuration => configuration with { Name = name }).Code);

    [Fact]
    public void A_name_longer_than_an_interface_name_is_refused() =>
        Assert.Equal(
            WireGuardErrorCodes.TunnelNameInvalid,
            Refuse(configuration => configuration with { Name = new string('a', 16) }).Code);

    [Fact]
    public void A_missing_private_key_is_refused() =>
        Assert.Equal(
            WireGuardErrorCodes.PrivateKeyMissing,
            Refuse(configuration => configuration with
            {
                Interface = configuration.Interface with { PrivateKey = SecretKey.Empty },
            }).Code);

    [Fact]
    public void A_malformed_private_key_is_refused()
    {
        var malformed = TestData.InterfacePrivateKey.TrimEnd('=');
        var error = Refuse(configuration => configuration with
        {
            Interface = configuration.Interface with { PrivateKey = SecretKey.FromValue(malformed) },
        });

        Assert.Equal(WireGuardErrorCodes.PrivateKeyInvalid, error.Code);
        Assert.DoesNotContain(malformed, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_interface_without_an_address_is_refused() =>
        Assert.Equal(
            WireGuardErrorCodes.AddressMissing,
            Refuse(configuration => configuration with
            {
                Interface = configuration.Interface with { Addresses = [] },
            }).Code);

    [Fact]
    public void An_address_with_an_out_of_range_prefix_is_refused() =>
        Assert.Equal(
            WireGuardErrorCodes.AddressInvalid,
            Refuse(configuration => configuration with
            {
                Interface = configuration.Interface with { Addresses = ["10.0.0.0/33"] },
            }).Code);

    [Fact]
    public void An_unusable_resolver_entry_is_refused() =>
        Assert.Equal(
            WireGuardErrorCodes.DnsInvalid,
            Refuse(configuration => configuration with
            {
                Interface = configuration.Interface with { Dns = ["not a host!"] },
            }).Code);

    [Theory]
    [InlineData(65536)]
    [InlineData(-1)]
    public void A_listen_port_outside_the_range_is_refused(int port) =>
        Assert.Equal(
            WireGuardErrorCodes.ListenPortInvalid,
            Refuse(configuration => configuration with
            {
                Interface = configuration.Interface with { ListenPort = port },
            }).Code);

    [Theory]
    [InlineData(0)]
    [InlineData(65535)]
    public void The_listen_port_boundaries_are_accepted(int port)
    {
        var result = TunnelValidator.Validate(TestData.ValidConfiguration() with
        {
            Interface = TestData.ValidConfiguration().Interface with { ListenPort = port },
        });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);
    }

    [Theory]
    [InlineData(575)]
    [InlineData(65536)]
    public void An_mtu_outside_the_range_is_refused(int mtu) =>
        Assert.Equal(
            WireGuardErrorCodes.MtuInvalid,
            Refuse(configuration => configuration with
            {
                Interface = configuration.Interface with { Mtu = mtu },
            }).Code);

    [Theory]
    [InlineData(576)]
    [InlineData(65535)]
    public void The_mtu_boundaries_are_accepted(int mtu)
    {
        var configuration = TestData.ValidConfiguration();

        var result = TunnelValidator.Validate(configuration with
        {
            Interface = configuration.Interface with { Mtu = mtu },
        });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);
    }

    [Fact]
    public void A_peer_without_a_public_key_is_refused()
    {
        var peer = TestData.ValidPeer();

        Assert.Equal(
            WireGuardErrorCodes.PeerPublicKeyMissing,
            Refuse(configuration => configuration with
            {
                Peers = [peer with { PublicKey = string.Empty }],
            }).Code);
    }

    [Fact]
    public void A_peer_with_a_malformed_public_key_is_refused()
    {
        var peer = TestData.ValidPeer();

        Assert.Equal(
            WireGuardErrorCodes.KeyInvalid,
            Refuse(configuration => configuration with
            {
                Peers = [peer with { PublicKey = "not-a-key" }],
            }).Code);
    }

    [Fact]
    public void The_same_peer_listed_twice_is_refused()
    {
        var peer = TestData.ValidPeer();

        var error = Refuse(configuration => configuration with { Peers = [peer, peer] });

        Assert.Equal(WireGuardErrorCodes.PeerPublicKeyDuplicate, error.Code);
        Assert.Contains(TestData.PeerOneKey, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_malformed_preshared_key_is_refused()
    {
        var peer = TestData.ValidPeer();

        Assert.Equal(
            WireGuardErrorCodes.PresharedKeyInvalid,
            Refuse(configuration => configuration with
            {
                Peers = [peer with { PresharedKey = SecretKey.FromValue("short") }],
            }).Code);
    }

    [Fact]
    public void A_peer_without_allowed_ips_is_refused()
    {
        var peer = TestData.ValidPeer();

        Assert.Equal(
            WireGuardErrorCodes.PeerAllowedIpsMissing,
            Refuse(configuration => configuration with
            {
                Peers = [peer with { AllowedIPs = [] }],
            }).Code);
    }

    [Fact]
    public void A_peer_with_an_invalid_allowed_ips_entry_is_refused()
    {
        var peer = TestData.ValidPeer();

        Assert.Equal(
            WireGuardErrorCodes.AllowedIpsInvalid,
            Refuse(configuration => configuration with
            {
                Peers = [peer with { AllowedIPs = ["10.0.0.0/33"] }],
            }).Code);
    }

    [Fact]
    public void A_peer_with_an_unusable_endpoint_is_refused()
    {
        var peer = TestData.ValidPeer();

        Assert.Equal(
            WireGuardErrorCodes.EndpointInvalid,
            Refuse(configuration => configuration with
            {
                Peers = [peer with { Endpoint = "vpn.example.com" }],
            }).Code);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    public void A_keepalive_outside_the_range_is_refused(int keepalive)
    {
        var peer = TestData.ValidPeer();

        Assert.Equal(
            WireGuardErrorCodes.KeepaliveInvalid,
            Refuse(configuration => configuration with
            {
                Peers = [peer with { PersistentKeepalive = keepalive }],
            }).Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65535)]
    public void The_keepalive_boundaries_are_accepted(int keepalive)
    {
        var configuration = TestData.ValidConfiguration();
        var peer = configuration.Peers[0];

        var result = TunnelValidator.Validate(configuration with
        {
            Peers = [peer with { PersistentKeepalive = keepalive }],
        });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);
    }

    [Fact]
    public void A_null_configuration_is_a_programming_error() =>
        Assert.Throws<ArgumentNullException>(() => TunnelValidator.Validate(null!));

    private static CoreError Refuse(Func<TunnelConfiguration, TunnelConfiguration> mutate)
    {
        var result = TunnelValidator.Validate(mutate(TestData.ValidConfiguration()));

        Assert.True(result.IsFailure, "the configuration was accepted but should have been refused");
        return result.Error;
    }
}
