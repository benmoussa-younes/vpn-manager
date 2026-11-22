using VpnManager.Core.Configuration;
using VpnManager.Core.Model;
using VpnManager.Core.Validation;

namespace VpnManager.Core.Tests;

/// <summary>
/// The routing-domain, table and fwmark rules the deployment uses, and the
/// round-trip that keeps the mode directives intact.
/// </summary>
public sealed class ConfigurationCompletenessTests
{
    [Theory]
    [InlineData("~example.com")]
    [InlineData("~unmanaged-bytes.com")]
    [InlineData("~internal")]
    public void A_routing_domain_is_a_valid_dns_entry(string value)
    {
        Assert.True(NetworkText.IsRoutingDomain(value));
        Assert.True(NetworkText.IsValidDnsEntry(value));
    }

    [Theory]
    [InlineData("~")]
    [InlineData("~ dns")]
    [InlineData("~bad!domain")]
    public void A_malformed_routing_domain_is_refused(string value)
    {
        Assert.False(NetworkText.IsRoutingDomain(value));
        Assert.False(NetworkText.IsValidDnsEntry(value));
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("off")]
    [InlineData("0")]
    [InlineData("1234")]
    [InlineData("4294967295")]
    public void A_usable_table_selector_is_accepted(string value) => Assert.True(NetworkText.IsValidTableValue(value));

    [Theory]
    [InlineData("nonsense")]
    [InlineData("-1")]
    [InlineData("4294967296")]
    [InlineData("")]
    public void An_unusable_table_selector_is_refused(string value) => Assert.False(NetworkText.IsValidTableValue(value));

    [Theory]
    [InlineData("off")]
    [InlineData("0")]
    [InlineData("51820")]
    [InlineData("0xca6c")]
    [InlineData("0xFFFFFFFF")]
    public void A_usable_fwmark_is_accepted(string value) => Assert.True(NetworkText.IsValidFwMark(value));

    [Theory]
    [InlineData("nonsense")]
    [InlineData("0x")]
    [InlineData("0xG")]
    [InlineData("0x100000000")]
    [InlineData("")]
    public void An_unusable_fwmark_is_refused(string value) => Assert.False(NetworkText.IsValidFwMark(value));

    [Fact]
    public void A_full_mode_configuration_with_routing_domain_and_fwmark_is_valid()
    {
        var configuration = FullModeConfiguration();

        Assert.True(TunnelValidator.Validate(configuration).IsSuccess);
    }

    [Fact]
    public void An_invalid_table_is_refused_with_its_code()
    {
        var configuration = FullModeConfiguration() with
        {
            Interface = FullModeConfiguration().Interface with { Table = "nonsense" },
        };

        var result = TunnelValidator.Validate(configuration);

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.TableInvalid, result.Error.Code);
    }

    [Fact]
    public void An_invalid_fwmark_is_refused_with_its_code()
    {
        var configuration = FullModeConfiguration() with
        {
            Interface = FullModeConfiguration().Interface with { FwMark = "nonsense" },
        };

        var result = TunnelValidator.Validate(configuration);

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.FwMarkInvalid, result.Error.Code);
    }

    [Fact]
    public void The_mode_directives_and_the_routing_domain_survive_a_round_trip()
    {
        var original = FullModeConfiguration();

        var text = WireGuardConfigWriter.Write(original);
        var parsed = WireGuardConfigParser.Parse(original.Name, text);

        Assert.True(parsed.IsSuccess);
        Assert.Equal("auto", parsed.Value.Interface.Table);
        Assert.Equal("0xca6c", parsed.Value.Interface.FwMark);
        Assert.Equal(["10.10.0.1", "~example.com"], parsed.Value.Interface.Dns);
        Assert.Equal(["0.0.0.0/0", "::/0"], parsed.Value.Peers[0].AllowedIPs);
    }

    private static TunnelConfiguration FullModeConfiguration()
    {
        var configuration = TestData.ValidConfiguration();
        return configuration with
        {
            Interface = configuration.Interface with
            {
                Dns = ["10.10.0.1", "~example.com"],
                Table = "auto",
                FwMark = "0xca6c",
            },
            Peers =
            [
                TestData.ValidPeer() with { AllowedIPs = ["0.0.0.0/0", "::/0"] },
            ],
        };
    }
}
