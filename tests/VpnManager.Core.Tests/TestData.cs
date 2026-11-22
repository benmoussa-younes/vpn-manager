using VpnManager.Core.Configuration;
using VpnManager.Core.Model;

namespace VpnManager.Core.Tests;

/// <summary>
/// Shared fixtures: canonical WireGuard keys (valid shape, never real secrets), a
/// realistic wg-quick file and realistic <c>wg show</c> output shapes. The
/// transport of the real machine is not used anywhere.
/// </summary>
internal static class TestData
{
    /// <summary>A private key of the right shape (32 bytes, base64, canonical).</summary>
    internal const string InterfacePrivateKey = "yAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBmk=";

    /// <summary>A first peer public key.</summary>
    internal const string PeerOneKey = "xTIBA5rboUvnH4htodjb6e697QjLERt1NAB4mZqp8Dg=";

    /// <summary>A second peer public key, distinct from the first.</summary>
    internal const string PeerTwoKey = "AAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBmk=";

    /// <summary>A preshared key of the right shape.</summary>
    internal const string PresharedKey = "FpCyhws9cxwWoV4xELtfJvjJN+zQVRPISllRWgeopVE=";

    /// <summary>A key used as the derived public key of the interface.</summary>
    internal const string DerivedPublicKey = "BAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBmk=";

    /// <summary>A configuration text shaped like the wg0.conf this machine uses.</summary>
    internal const string SampleConfigText = """
        [Interface]
        # Name = wg0
        Address = 10.10.0.11/32
        DNS = 10.10.0.1
        MTU = 1420
        Table = auto
        PrivateKey = yAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBmk=
        ListenPort = 51820
        PreUp = WG_INTERFACE=wg0 /usr/local/sbin/kill-switch.sh check
        PostUp = WG_INTERFACE=wg0 /usr/local/sbin/kill-switch.sh add
        PostDown = WG_INTERFACE=wg0 /usr/local/sbin/kill-switch.sh delete

        [Peer]
        # Name = peer-one
        PublicKey = xTIBA5rboUvnH4htodjb6e697QjLERt1NAB4mZqp8Dg=
        AllowedIPs = 10.10.0.0/24
        Endpoint = vpn.example.com:51820
        PersistentKeepalive = 25

        [Peer]
        # Name = peer-two
        PublicKey = AAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBmk=
        PresharedKey = FpCyhws9cxwWoV4xELtfJvjJN+zQVRPISllRWgeopVE=
        AllowedIPs = 10.10.0.0/24, 0.0.0.0/0
        """;

    /// <summary>Output shaped like <c>wg show wg0</c> on a running interface.</summary>
    internal const string SampleShowText = """
        interface: wg0
          public key: xTIBA5rboUvnH4htodjb6e697QjLERt1NAB4mZqp8Dg=
          private key: (hidden)
          listening port: 51820

        peer: AAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBmk=
          preshared key: (hidden)
          endpoint: vpn.example.com:51820
          allowed ips: 10.10.0.0/24, 0.0.0.0/0
          latest handshake: 1 minute, 20 seconds ago
          transfer: 1.21 KiB received, 3.50 MiB sent
          persistent keepalive: every 25 seconds

        peer: BAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBmk=
          endpoint: 203.0.113.9:51820
          allowed ips: 10.10.0.0/24
          latest handshake: Now
          transfer: 0 B received, 0 B sent
          persistent keepalive: off
        """;

    /// <summary>Builds a valid configuration with one peer, ready to be adjusted.</summary>
    internal static TunnelConfiguration ValidConfiguration() => new()
    {
        Name = "wg0",
        Interface = new InterfaceSettings
        {
            PrivateKey = SecretKey.FromValue(InterfacePrivateKey),
            Addresses = ["10.10.0.11/32"],
            Dns = ["10.10.0.1"],
            Mtu = 1420,
            ListenPort = 51820,
        },
        Peers =
        [
            new PeerConfiguration
            {
                PublicKey = PeerOneKey,
                AllowedIPs = ["10.10.0.0/24"],
                Endpoint = "vpn.example.com:51820",
                PersistentKeepalive = 25,
            },
        ],
    };

    /// <summary>Builds the peer used by the validator tests.</summary>
    internal static PeerConfiguration ValidPeer() => new()
    {
        PublicKey = PeerOneKey,
        AllowedIPs = ["0.0.0.0/0"],
        Endpoint = "vpn.example.com:51820",
        PersistentKeepalive = 25,
    };

    /// <summary>Parses the sample file, failing the calling test when it does not parse.</summary>
    internal static TunnelConfiguration SampleConfiguration()
    {
        var parsed = WireGuardConfigParser.Parse("wg0", SampleConfigText);
        Xunit.Assert.True(parsed.IsSuccess, parsed.IsFailure ? parsed.Error.Message : string.Empty);
        return parsed.Value;
    }
}
