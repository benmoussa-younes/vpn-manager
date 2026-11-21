using Core.Results;
using VpnManager.Core.Configuration;
using VpnManager.Core.Model;
using VpnManager.Core.Presentation;
using VpnManager.Core.Runtime;
using VpnManager.Core.Tests.Fakes;

namespace VpnManager.Core.Tests;

/// <summary>
/// The configuration keys and malformed shapes that the main-path tests do not
/// reach: the remaining wg-quick keys and the refusal branches of the parsers.
/// </summary>
public sealed class ConfigurationEdgeCaseTests
{
    [Fact]
    public void Pre_down_and_post_down_hooks_are_read_and_written_in_order()
    {
        const string text = """
            [Interface]
            PrivateKey = yAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBmk=
            Address = 10.10.0.11/32
            PreDown = before
            PostDown = after
            """;

        var parsed = WireGuardConfigParser.Parse("wg0", text);

        Assert.True(parsed.IsSuccess, parsed.IsFailure ? parsed.Error.Message : string.Empty);
        Assert.Equal(["before"], parsed.Value.Interface.PreDown);
        Assert.Equal(["after"], parsed.Value.Interface.PostDown);

        var written = WireGuardConfigWriter.Write(parsed.Value);
        Assert.Contains("PreDown = before", written, StringComparison.Ordinal);
        Assert.Contains("PostDown = after", written, StringComparison.Ordinal);
    }

    [Fact]
    public void Firewall_mark_and_save_config_are_written_when_set()
    {
        var sample = TestData.SampleConfiguration();
        var configuration = sample with
        {
            Interface = sample.Interface with { FwMark = "0x1234", SaveConfig = true, PreDown = ["down-hook"], PostDown = ["after-hook"] },
        };

        var written = WireGuardConfigWriter.Write(configuration);

        Assert.Contains("FwMark = 0x1234", written, StringComparison.Ordinal);
        Assert.Contains("SaveConfig = true", written, StringComparison.Ordinal);
        Assert.Contains("PreDown = down-hook", written, StringComparison.Ordinal);
        Assert.Contains("PostDown = after-hook", written, StringComparison.Ordinal);

        var reparsed = WireGuardConfigParser.Parse("wg0", written);
        Assert.True(reparsed.IsSuccess, reparsed.IsFailure ? reparsed.Error.Message : string.Empty);
        Assert.Equal("0x1234", reparsed.Value.Interface.FwMark);
        Assert.True(reparsed.Value.Interface.SaveConfig);
    }

    [Fact]
    public void Save_config_false_is_written_as_false()
    {
        var sample = TestData.SampleConfiguration();

        var written = WireGuardConfigWriter.Write(sample with
        {
            Interface = sample.Interface with { SaveConfig = false },
        });

        Assert.Contains("SaveConfig = false", written, StringComparison.Ordinal);
    }

    [Fact]
    public void A_malformed_name_comment_is_not_treated_as_a_label()
    {
        const string text = """
            [Interface]
            PrivateKey = yAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBmk=
            Address = 10.10.0.11/32

            [Peer]
            # Name
            # Name =
            PublicKey = xTIBA5rboUvnH4htodjb6e697QjLERt1NAB4mZqp8Dg=
            AllowedIPs = 10.10.0.0/24
            """;

        var parsed = WireGuardConfigParser.Parse("wg0", text);

        Assert.True(parsed.IsSuccess, parsed.IsFailure ? parsed.Error.Message : string.Empty);
        Assert.Null(parsed.Value.Peers[0].Label);
    }

    [Fact]
    public void An_unknown_peer_key_is_refused_with_its_line_number()
    {
        const string text = """
            [Interface]
            PrivateKey = yAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBmk=
            Address = 10.10.0.11/32

            [Peer]
            PublicKey = xTIBA5rboUvnH4htodjb6e697QjLERt1NAB4mZqp8Dg=
            AllowedIPs = 10.10.0.0/24
            Foo = bar
            """;

        var parsed = WireGuardConfigParser.Parse("wg0", text);

        Assert.True(parsed.IsFailure);
        Assert.Equal(WireGuardErrorCodes.ConfigUnsupportedKey, parsed.Error.Code);
        Assert.Contains("Line 8", parsed.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_non_numeric_mtu_is_refused_with_its_line_number()
    {
        const string text = """
            [Interface]
            PrivateKey = yAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBmk=
            Address = 10.10.0.11/32
            MTU = huge
            """;

        var parsed = WireGuardConfigParser.Parse("wg0", text);

        Assert.True(parsed.IsFailure);
        Assert.Equal(WireGuardErrorCodes.ConfigParseError, parsed.Error.Code);
        Assert.Contains("Line 4", parsed.Error.Message, StringComparison.Ordinal);
        Assert.Contains("MTU", parsed.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_label_comment_with_no_equals_and_no_content_is_never_a_peer()
    {
        const string text = """
            [Interface]
            PrivateKey = yAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBmk=
            Address = 10.10.0.11/32

            # Name = stray
            [Peer]
            PublicKey = xTIBA5rboUvnH4htodjb6e697QjLERt1NAB4mZqp8Dg=
            AllowedIPs = 10.10.0.0/24
            """;

        var parsed = WireGuardConfigParser.Parse("wg0", text);

        Assert.True(parsed.IsSuccess);
        Assert.Equal("stray", parsed.Value.Peers[0].Label);
    }

    [Fact]
    public void Crlf_line_endings_parse_the_same_as_lf()
    {
        var crlf = TestData.SampleConfigText.Replace("\n", "\r\n", StringComparison.Ordinal);

        var parsed = WireGuardConfigParser.Parse("wg0", crlf);

        Assert.True(parsed.IsSuccess, parsed.IsFailure ? parsed.Error.Message : string.Empty);
        Assert.Equal("wg0", parsed.Value.Name);
        Assert.Equal(2, parsed.Value.Peers.Count);
        Assert.Equal("peer-one", parsed.Value.Peers[0].Label);
        Assert.Equal(51820, parsed.Value.Interface.ListenPort);
    }

    [Fact]
    public void A_second_interface_section_is_refused_rather_than_merged()
    {
        const string text = """
            [Interface]
            PrivateKey = yAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBmk=
            Address = 10.10.0.11/32
            MTU = 1420

            [Interface]
            MTU = 900
            """;

        var parsed = WireGuardConfigParser.Parse("wg0", text);

        Assert.True(parsed.IsFailure);
        Assert.Equal(WireGuardErrorCodes.ConfigParseError, parsed.Error.Code);
        Assert.Contains("Line 6", parsed.Error.Message, StringComparison.Ordinal);
        Assert.Contains("[Interface]", parsed.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_inline_comment_is_stripped_from_a_value()
    {
        const string text = """
            [Interface]
            PrivateKey = yAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBmk=
            Address = 10.10.0.11/32 # the client address
            MTU = 1420 # the usual WireGuard MTU

            [Peer]
            PublicKey = xTIBA5rboUvnH4htodjb6e697QjLERt1NAB4mZqp8Dg=
            AllowedIPs = 10.10.0.0/24 # the tunnel subnet
            """;

        var parsed = WireGuardConfigParser.Parse("wg0", text);

        Assert.True(parsed.IsSuccess, parsed.IsFailure ? parsed.Error.Message : string.Empty);
        Assert.Equal(["10.10.0.11/32"], parsed.Value.Interface.Addresses);
        Assert.Equal(1420, parsed.Value.Interface.Mtu);
        Assert.Equal(["10.10.0.0/24"], parsed.Value.Peers[0].AllowedIPs);
    }

    [Fact]
    public void A_peer_with_no_keys_parses_but_the_validator_refuses_it()
    {
        const string text = """
            [Interface]
            PrivateKey = yAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBmk=
            Address = 10.10.0.11/32

            [Peer]
            """;

        var parsed = WireGuardConfigParser.Parse("wg0", text);

        Assert.True(parsed.IsSuccess, parsed.IsFailure ? parsed.Error.Message : string.Empty);
        var peer = Assert.Single(parsed.Value.Peers);
        Assert.Equal(string.Empty, peer.PublicKey);

        var validated = VpnManager.Core.Validation.TunnelValidator.Validate(parsed.Value);
        Assert.True(validated.IsFailure);
        Assert.Equal(WireGuardErrorCodes.PeerPublicKeyMissing, validated.Error.Code);
    }

    [Fact]
    public void A_very_long_value_is_a_value_not_a_crash()
    {
        var longHost = new string('a', 100_000);
        var text = $"""
            [Interface]
            PrivateKey = yAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBmk=
            Address = 10.10.0.11/32
            Table = {longHost}
            """;

        var parsed = WireGuardConfigParser.Parse("wg0", text);

        Assert.True(parsed.IsSuccess);
        Assert.Equal(100_000, parsed.Value.Interface.Table!.Length);
    }

    [Fact]
    public void Two_peer_keys_that_differ_only_in_case_are_two_different_keys()
    {
        var sample = TestData.SampleConfiguration();
        var first = sample.Peers[0];
        var upper = first with { PublicKey = first.PublicKey.ToUpperInvariant() };

        Assert.NotEqual(first.PublicKey, upper.PublicKey);

        var validated = VpnManager.Core.Validation.TunnelValidator.Validate(sample with
        {
            Peers = [first, upper],
        });

        Assert.True(validated.IsSuccess, validated.IsFailure ? validated.Error.Message : string.Empty);
    }

    [Fact]
    public void Every_editor_field_key_is_mapped_from_a_validation_code()
    {
        Assert.Equal(EditorField.Name, EditorField.FromErrorCode(WireGuardErrorCodes.TunnelNameInvalid));
        Assert.Equal(EditorField.PrivateKey, EditorField.FromErrorCode(WireGuardErrorCodes.PrivateKeyMissing));
        Assert.Equal(EditorField.Addresses, EditorField.FromErrorCode(WireGuardErrorCodes.AddressMissing));
        Assert.Equal(EditorField.Dns, EditorField.FromErrorCode(WireGuardErrorCodes.DnsInvalid));
        Assert.Equal(EditorField.Mtu, EditorField.FromErrorCode(WireGuardErrorCodes.MtuInvalid));
        Assert.Equal(EditorField.PeerPublicKey, EditorField.FromErrorCode(WireGuardErrorCodes.PeerPublicKeyMissing));
        Assert.Equal(EditorField.PeerPublicKey, EditorField.FromErrorCode(WireGuardErrorCodes.PeerPublicKeyDuplicate));
        Assert.Equal(EditorField.PeerPresharedKey, EditorField.FromErrorCode(WireGuardErrorCodes.PresharedKeyInvalid));
        Assert.Equal(EditorField.PeerAllowedIps, EditorField.FromErrorCode(WireGuardErrorCodes.PeerAllowedIpsMissing));
        Assert.Equal(EditorField.PeerKeepalive, EditorField.FromErrorCode(WireGuardErrorCodes.KeepaliveInvalid));
    }
}

/// <summary>The refusal branches of the <c>wg show</c> text parser.</summary>
public sealed class ShowParserEdgeCaseTests
{
    [Theory]
    [InlineData("latest handshake: 1 minute, 20 ago")]
    [InlineData("latest handshake: soon ago")]
    [InlineData("latest handshake: 1 fortnight ago")]
    [InlineData("latest handshake: no clock here")]
    public void An_unparsable_handshake_keeps_the_text_and_reports_no_age(string line)
    {
        var peer = ParsePeer(line);

        Assert.Null(peer.LatestHandshakeAge);
        Assert.NotNull(peer.LatestHandshakeText);
    }

    [Theory]
    [InlineData("transfer: 1.21 KiB received, 3.50 MiB")]
    [InlineData("transfer: abc KiB received, 1.00 KiB sent")]
    [InlineData("transfer: 1.21 XB received, 1.00 KiB sent")]
    [InlineData("transfer: 1.21 KiB sent, 3.50 MiB received")]
    public void An_unparsable_transfer_leaves_the_counters_at_zero(string line)
    {
        var peer = ParsePeer(line);

        Assert.Equal(0, peer.Received.Bytes);
        Assert.Equal(0, peer.Sent.Bytes);
    }

    [Fact]
    public void A_negative_handshake_unit_is_parsed_as_a_negative_age()
    {
        var peer = ParsePeer("latest handshake: -1 minutes ago");

        Assert.Equal(TimeSpan.FromMinutes(-1), peer.LatestHandshakeAge);
    }

    private static WireGuardPeer ParsePeer(string peerLine)
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

/// <summary>The inspector paths where the command or the text does not match.</summary>
public sealed class RuntimeInspectorEdgeCaseTests
{
    [Fact]
    public async Task Reading_a_renamed_interface_falls_back_to_the_single_value()
    {
        var runner = FakeWireGuardCommandRunner.Returning("""
            interface: wg1
              public key: xTIBA5rboUvnH4htodjb6e697QjLERt1NAB4mZqp8Dg=
              listening port: 51820
            """);
        var inspector = new WireGuardRuntimeInspector(runner);

        var result = await inspector.GetInterfaceAsync("wg9");

        Assert.True(result.IsSuccess);
        Assert.Equal("wg1", result.Value.Name);
    }

    [Fact]
    public async Task A_failed_public_key_derivation_propagates()
    {
        var runner = FakeWireGuardCommandRunner.Failing(new CoreError(WireGuardErrorCodes.PermissionDenied, "denied"));
        var inspector = new WireGuardRuntimeInspector(runner);

        var result = await inspector.DerivePublicKeyAsync(SecretKey.FromValue(TestData.InterfacePrivateKey));

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.PermissionDenied, result.Error.Code);
    }

    [Fact]
    public async Task A_failed_key_generation_propagates()
    {
        var runner = FakeWireGuardCommandRunner.Failing(new CoreError(WireGuardErrorCodes.ToolsUnavailable, "no wg"));
        var inspector = new WireGuardRuntimeInspector(runner);

        Assert.True((await inspector.GeneratePrivateKeyAsync()).IsFailure);
        Assert.True((await inspector.GeneratePresharedKeyAsync()).IsFailure);
    }

    [Fact]
    public async Task An_unparsable_interface_output_is_refused()
    {
        var runner = FakeWireGuardCommandRunner.Returning("this is not wg output");
        var inspector = new WireGuardRuntimeInspector(runner);

        var result = await inspector.GetInterfaceAsync("wg0");

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.ParseError, result.Error.Code);
    }
}
