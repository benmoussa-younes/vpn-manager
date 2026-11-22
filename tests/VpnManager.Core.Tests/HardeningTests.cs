using System.Text;
using VpnManager.Core.Configuration;
using VpnManager.Core.Model;
using VpnManager.Core.NetworkManager;
using VpnManager.Core.Process;
using VpnManager.Core.Services;
using VpnManager.Core.State;
using VpnManager.Core.Tests.Fakes;
using VpnManager.Core.Validation;

namespace VpnManager.Core.Tests;

/// <summary>Adversarial inputs for the parser, the policy service and the nmcli seam.</summary>
public sealed class HardeningTests
{
    [Fact]
    public void The_parser_refuses_text_over_its_limit()
    {
        var parsed = WireGuardConfigParser.Parse("wg0", new string('a', WireGuardConfigParser.MaximumTextLength + 1));

        Assert.True(parsed.IsFailure);
        Assert.Equal(WireGuardErrorCodes.ConfigParseError, parsed.Error.Code);
    }

    [Fact]
    public void The_parser_refuses_too_many_lines()
    {
        var text = new string('\n', WireGuardConfigParser.MaximumLines + 1);

        var parsed = WireGuardConfigParser.Parse("wg0", text);

        Assert.True(parsed.IsFailure);
        Assert.Equal(WireGuardErrorCodes.ConfigParseError, parsed.Error.Code);
    }

    [Fact]
    public void The_parser_refuses_too_many_peers()
    {
        var builder = new StringBuilder("[Interface]\nPrivateKey = ");
        builder.Append(TestData.InterfacePrivateKey).Append('\n');
        for (var index = 0; index <= WireGuardConfigParser.MaximumPeers; index++)
        {
            builder.Append("[Peer]\n");
        }

        var parsed = WireGuardConfigParser.Parse("wg0", builder.ToString());

        Assert.True(parsed.IsFailure);
        Assert.Equal(WireGuardErrorCodes.ConfigParseError, parsed.Error.Code);
    }

    [Fact]
    public void ReadDeclaredNameRefusesAnOversizedText()
    {
        Assert.Null(WireGuardConfigParser.ReadDeclaredName(new string('a', WireGuardConfigParser.MaximumTextLength + 1)));
    }

    [Theory]
    [InlineData("1.1.1.1;touch /tmp/x", false)]
    [InlineData("a,b", false)]
    [InlineData("~", false)]
    [InlineData("~corp.example", true)]
    [InlineData("corp.example", true)]
    [InlineData("2001:db8::1", true)]
    public void A_dns_entry_is_validated_before_it_reaches_an_argument(string entry, bool expected)
    {
        Assert.Equal(expected, NetworkText.IsValidDnsEntry(entry));
    }

    [Fact]
    public async Task NmcliRefusesAnInjectedDnsEntryBeforeLaunch()
    {
        var runner = new FakeProcessRunner(_ => ProcessResult.Completed(0, string.Empty, string.Empty));
        var connections = new NmcliNetworkManagerConnections(runner);

        var result = await connections.ApplyDnsAsync("wg0", ["1.1.1.1;touch /tmp/x"], [], NetworkManagerDnsMode.Profile);

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.DnsInvalid, result.Error.Code);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task NmcliRefusesAnOutOfRangeAutoconnectPriorityBeforeLaunch()
    {
        var runner = new FakeProcessRunner(_ => ProcessResult.Completed(0, string.Empty, string.Empty));
        var connections = new NmcliNetworkManagerConnections(runner);

        var result = await connections.SetAutoConnectAsync("wg0", autoConnect: true, priority: 1_000);

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.NetworkManagerInvalidPriority, result.Error.Code);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task NmcliRefusesAnOptionShapedNameForDnsBeforeLaunch()
    {
        var runner = new FakeProcessRunner(_ => ProcessResult.Completed(0, string.Empty, string.Empty));
        var connections = new NmcliNetworkManagerConnections(runner);

        var result = await connections.ApplyDnsAsync("-evil", [], [], NetworkManagerDnsMode.Profile);

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.NetworkManagerInvalidConnection, result.Error.Code);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task TheServiceRefusesAnInvalidSearchDomain()
    {
        var service = CreateService(out var state);
        state.Files["wg0"] = TestData.SampleConfigText;

        var saved = await service.SetPolicyAsync(
            "wg0",
            new TunnelPolicy(DnsSearchDomains: ["1.1.1.1;touch /tmp/x"]));

        Assert.True(saved.IsFailure);
        Assert.Equal(WireGuardErrorCodes.DnsInvalid, saved.Error.Code);
    }

    [Fact]
    public async Task TheServiceRefusesAnUndefinedDnsMode()
    {
        var service = CreateService(out var state);
        state.Files["wg0"] = TestData.SampleConfigText;

        var saved = await service.SetPolicyAsync("wg0", new TunnelPolicy(DnsMode: (NetworkManagerDnsMode)99));

        Assert.True(saved.IsFailure);
        Assert.Equal(WireGuardErrorCodes.DnsInvalid, saved.Error.Code);
    }

    [Fact]
    public async Task TheServiceRefusesAPolicyForAnUnknownProfile()
    {
        var service = CreateService(out _);

        var saved = await service.SetPolicyAsync("absent", new TunnelPolicy());

        Assert.True(saved.IsFailure);
        Assert.Equal(WireGuardErrorCodes.ConfigNotFound, saved.Error.Code);
    }

    private static TunnelService CreateService(out FakeWireGuardConfigStore store)
    {
        store = new FakeWireGuardConfigStore();
        return new TunnelService(
            store,
            new FakeWireGuardRuntimeInspector(),
            TunnelScope.User,
            new FakeApplicationStateStore(),
            new FakeNetworkManagerConnections(),
            "alice");
    }
}
