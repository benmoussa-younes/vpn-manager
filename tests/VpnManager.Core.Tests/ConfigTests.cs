using Core.Results;
using VpnManager.Core.Configuration;
using VpnManager.Core.Model;

namespace VpnManager.Core.Tests;

/// <summary>
/// The wg-quick text format: a realistic file round-trips unchanged, and every
/// malformed shape is refused with the line number named.
/// </summary>
public sealed class WireGuardConfigParserTests
{
    [Fact]
    public void A_realistic_file_parses_field_by_field()
    {
        var configuration = TestData.SampleConfiguration();

        Assert.Equal("wg0", configuration.Name);
        Assert.Equal(TestData.InterfacePrivateKey, configuration.Interface.PrivateKey.Reveal());
        Assert.Equal(["10.10.0.11/32"], configuration.Interface.Addresses);
        Assert.Equal(["10.10.0.1"], configuration.Interface.Dns);
        Assert.Equal(1420, configuration.Interface.Mtu);
        Assert.Equal(51820, configuration.Interface.ListenPort);
        Assert.Equal("auto", configuration.Interface.Table);
        Assert.Equal(["WG_INTERFACE=wg0 /usr/local/sbin/kill-switch.sh check"], configuration.Interface.PreUp);
        Assert.Equal(["WG_INTERFACE=wg0 /usr/local/sbin/kill-switch.sh add"], configuration.Interface.PostUp);
        Assert.Equal(["WG_INTERFACE=wg0 /usr/local/sbin/kill-switch.sh delete"], configuration.Interface.PostDown);
        Assert.Equal(2, configuration.Peers.Count);

        var first = configuration.Peers[0];
        Assert.Equal("peer-one", first.Label);
        Assert.Equal(TestData.PeerOneKey, first.PublicKey);
        Assert.Null(first.PresharedKey);
        Assert.Equal(["10.10.0.0/24"], first.AllowedIPs);
        Assert.Equal("vpn.example.com:51820", first.Endpoint);
        Assert.Equal(25, first.PersistentKeepalive);

        var second = configuration.Peers[1];
        Assert.Equal("peer-two", second.Label);
        Assert.Equal(TestData.PeerTwoKey, second.PublicKey);
        Assert.NotNull(second.PresharedKey);
        Assert.Equal(TestData.PresharedKey, second.PresharedKey!.Reveal());
        Assert.Equal(["10.10.0.0/24", "0.0.0.0/0"], second.AllowedIPs);
        Assert.Null(second.Endpoint);
    }

    [Fact]
    public void Writing_and_reading_again_preserves_the_configuration()
    {
        var original = TestData.SampleConfiguration();

        var text = WireGuardConfigWriter.Write(original);
        var reparsed = WireGuardConfigParser.Parse(original.Name, text);

        Assert.True(reparsed.IsSuccess, reparsed.IsFailure ? reparsed.Error.Message : string.Empty);
        AssertSame(original, reparsed.Value);
    }

    [Fact]
    public void A_peer_label_before_the_header_is_read_too()
    {
        const string text = """
            [Interface]
            PrivateKey = yAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBmk=
            Address = 10.10.0.11/32

            # Name = labelled-before
            [Peer]
            PublicKey = xTIBA5rboUvnH4htodjb6e697QjLERt1NAB4mZqp8Dg=
            AllowedIPs = 10.10.0.0/24
            """;

        var parsed = WireGuardConfigParser.Parse("wg0", text);

        Assert.True(parsed.IsSuccess);
        Assert.Equal("labelled-before", parsed.Value.Peers[0].Label);
    }

    [Fact]
    public void Repeated_keys_accumulate_in_file_order()
    {
        const string text = """
            [Interface]
            PrivateKey = yAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBmk=
            Address = 10.10.0.11/32
            Address = 2001:db8::11/128
            DNS = 10.10.0.1, 10.10.0.2
            PostUp = first
            PostUp = second
            """;

        var parsed = WireGuardConfigParser.Parse("wg0", text);

        Assert.True(parsed.IsSuccess);
        Assert.Equal(["10.10.0.11/32", "2001:db8::11/128"], parsed.Value.Interface.Addresses);
        Assert.Equal(["10.10.0.1", "10.10.0.2"], parsed.Value.Interface.Dns);
        Assert.Equal(["first", "second"], parsed.Value.Interface.PostUp);
    }

    [Fact]
    public void Section_headers_are_case_insensitive_and_comments_are_ignored()
    {
        const string text = """
            # a leading comment
            [interface]
            privatekey = yAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBmk=
            address = 10.10.0.11/32

            [peer]
            publickey = xTIBA5rboUvnH4htodjb6e697QjLERt1NAB4mZqp8Dg=
            allowedips = 10.10.0.0/24
            """;

        var parsed = WireGuardConfigParser.Parse("wg0", text);

        Assert.True(parsed.IsSuccess, parsed.IsFailure ? parsed.Error.Message : string.Empty);
        Assert.Single(parsed.Value.Peers);
    }

    [Fact]
    public void Save_config_is_read_as_a_boolean()
    {
        const string text = """
            [Interface]
            PrivateKey = yAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBmk=
            Address = 10.10.0.11/32
            SaveConfig = true
            FwMark = 0x1234
            """;

        var parsed = WireGuardConfigParser.Parse("wg0", text);

        Assert.True(parsed.IsSuccess);
        Assert.True(parsed.Value.Interface.SaveConfig);
        Assert.Equal("0x1234", parsed.Value.Interface.FwMark);
    }

    [Fact]
    public void An_unknown_key_is_refused_with_its_line_number()
    {
        var error = Refuse("""
            [Interface]
            PrivateKey = yAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBmk=
            Address = 10.10.0.11/32
            Foo = bar
            """);

        Assert.Equal(WireGuardErrorCodes.ConfigUnsupportedKey, error.Code);
        Assert.Contains("Line 4", error.Message, StringComparison.Ordinal);

        // The key text is deliberately not echoed: a malformed line can put key
        // material where a key name would be, and a message may be shown or logged.
        Assert.DoesNotContain("Foo", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_line_without_an_equals_sign_is_refused_with_its_line_number()
    {
        var error = Refuse("""
            [Interface]
            PrivateKey = yAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBmk=
            Address 10.10.0.11/32
            """);

        Assert.Equal(WireGuardErrorCodes.ConfigParseError, error.Code);
        Assert.Contains("Line 3", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_non_numeric_listen_port_is_refused_with_its_line_number()
    {
        var error = Refuse("""
            [Interface]
            PrivateKey = yAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBmk=
            Address = 10.10.0.11/32
            ListenPort = abc
            """);

        Assert.Equal(WireGuardErrorCodes.ConfigParseError, error.Code);
        Assert.Contains("Line 4", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_non_numeric_keepalive_is_refused()
    {
        var error = Refuse("""
            [Interface]
            PrivateKey = yAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBmk=
            Address = 10.10.0.11/32

            [Peer]
            PublicKey = xTIBA5rboUvnH4htodjb6e697QjLERt1NAB4mZqp8Dg=
            AllowedIPs = 10.10.0.0/24
            PersistentKeepalive = soon
            """);

        Assert.Equal(WireGuardErrorCodes.ConfigParseError, error.Code);
        Assert.Contains("Line 8", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_non_boolean_save_config_is_refused()
    {
        var error = Refuse("""
            [Interface]
            PrivateKey = yAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBmk=
            Address = 10.10.0.11/32
            SaveConfig = maybe
            """);

        Assert.Equal(WireGuardErrorCodes.ConfigParseError, error.Code);
        Assert.Contains("Line 4", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_without_an_interface_section_is_refused()
    {
        var error = Refuse("""
            [Peer]
            PublicKey = xTIBA5rboUvnH4htodjb6e697QjLERt1NAB4mZqp8Dg=
            AllowedIPs = 10.10.0.0/24
            """);

        Assert.Equal(WireGuardErrorCodes.ConfigParseError, error.Code);
        Assert.Contains("Interface", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_text_is_refused()
    {
        var error = Refuse(string.Empty);

        Assert.Equal(WireGuardErrorCodes.ConfigParseError, error.Code);
    }

    [Fact]
    public void Null_text_is_refused()
    {
        var parsed = WireGuardConfigParser.Parse("wg0", null);

        Assert.True(parsed.IsFailure);
        Assert.Equal(WireGuardErrorCodes.ConfigParseError, parsed.Error.Code);
    }

    private static CoreError Refuse(string text)
    {
        var parsed = WireGuardConfigParser.Parse("wg0", text);

        Assert.True(parsed.IsFailure, "the text parsed but should have been refused");
        return parsed.Error;
    }

    private static void AssertSame(TunnelConfiguration expected, TunnelConfiguration actual)
    {
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Interface.PrivateKey, actual.Interface.PrivateKey);
        Assert.Equal(expected.Interface.Addresses, actual.Interface.Addresses);
        Assert.Equal(expected.Interface.Dns, actual.Interface.Dns);
        Assert.Equal(expected.Interface.ListenPort, actual.Interface.ListenPort);
        Assert.Equal(expected.Interface.Mtu, actual.Interface.Mtu);
        Assert.Equal(expected.Interface.Table, actual.Interface.Table);
        Assert.Equal(expected.Interface.FwMark, actual.Interface.FwMark);
        Assert.Equal(expected.Interface.SaveConfig, actual.Interface.SaveConfig);
        Assert.Equal(expected.Interface.PreUp, actual.Interface.PreUp);
        Assert.Equal(expected.Interface.PostUp, actual.Interface.PostUp);
        Assert.Equal(expected.Interface.PreDown, actual.Interface.PreDown);
        Assert.Equal(expected.Interface.PostDown, actual.Interface.PostDown);
        Assert.Equal(expected.Peers.Count, actual.Peers.Count);

        for (var index = 0; index < expected.Peers.Count; index++)
        {
            var expectedPeer = expected.Peers[index];
            var actualPeer = actual.Peers[index];

            Assert.Equal(expectedPeer.Label, actualPeer.Label);
            Assert.Equal(expectedPeer.PublicKey, actualPeer.PublicKey);
            Assert.Equal(expectedPeer.PresharedKey, actualPeer.PresharedKey);
            Assert.Equal(expectedPeer.AllowedIPs, actualPeer.AllowedIPs);
            Assert.Equal(expectedPeer.Endpoint, actualPeer.Endpoint);
            Assert.Equal(expectedPeer.PersistentKeepalive, actualPeer.PersistentKeepalive);
        }
    }
}

/// <summary>The canonical writer, including the shape of its output.</summary>
public sealed class WireGuardConfigWriterTests
{
    [Fact]
    public void The_writer_emits_the_private_key_exactly_when_it_is_set()
    {
        var text = WireGuardConfigWriter.Write(TestData.ValidConfiguration());

        Assert.Contains($"PrivateKey = {TestData.InterfacePrivateKey}", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_writer_skips_a_key_line_that_has_no_value()
    {
        var configuration = TestData.ValidConfiguration();

        var text = WireGuardConfigWriter.Write(configuration with
        {
            Interface = configuration.Interface with { PrivateKey = SecretKey.Empty },
        });

        Assert.DoesNotContain("PrivateKey", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_writer_writes_one_line_per_peer_and_its_label()
    {
        var text = WireGuardConfigWriter.Write(TestData.SampleConfiguration());

        Assert.Contains("[Peer]", text, StringComparison.Ordinal);
        Assert.Contains("# Name = peer-one", text, StringComparison.Ordinal);
        Assert.Contains("# Name = peer-two", text, StringComparison.Ordinal);
        Assert.EndsWith("\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_configuration_still_writes_its_section()
    {
        var text = WireGuardConfigWriter.Write(new TunnelConfiguration { Name = "empty" });

        Assert.Contains("[Interface]", text, StringComparison.Ordinal);
        Assert.Contains("# Name = empty", text, StringComparison.Ordinal);
        Assert.DoesNotContain("[Peer]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_null_configuration_is_a_programming_error() =>
        Assert.Throws<ArgumentNullException>(() => WireGuardConfigWriter.Write(null!));
}
