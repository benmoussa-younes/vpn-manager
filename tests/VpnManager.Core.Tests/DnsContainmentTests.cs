using VpnManager.Core.Process;
using VpnManager.Core.Runtime;
using VpnManager.Core.Tests.Fakes;

namespace VpnManager.Core.Tests;

/// <summary>
/// The read-only DNS containment probe: the <c>resolvectl</c> text becomes a state,
/// and the state becomes an honest verdict. An uncontained link is a warning, never
/// a false "protected".
/// </summary>
public sealed class DnsContainmentTests
{
    [Fact]
    public void A_full_tunnel_with_the_route_only_domain_is_contained()
    {
        const string status = """
            Link 11 (wg-CH-ES-2)
                Current Scopes: DNS
                     Protocols: +DefaultRoute -LLMNR -mDNS
            Current DNS Server: 10.2.0.1
                   DNS Servers: 10.2.0.1
                    DNS Domain: ~.
                 Default Route: yes
            """;

        Assert.True(ResolvectlOutputParser.TryParse(status, out var state));
        Assert.Equal(["10.2.0.1"], state.Servers);
        Assert.Equal(["~."], state.Domains);
        Assert.True(state.DefaultRoute);
        Assert.Equal(DnsContainment.Contained, DnsContainmentPolicy.Classify(state));
        Assert.Contains("through the tunnel", DnsContainmentPolicy.Describe(DnsContainment.Contained), StringComparison.Ordinal);
    }

    [Fact]
    public void Specific_domains_are_split_and_no_domain_is_uncontained()
    {
        Assert.Equal(
            DnsContainment.Split,
            DnsContainmentPolicy.Classify(new DnsState(["10.0.0.1"], ["~corp.example"], false)));
        Assert.Equal(
            DnsContainment.Uncontained,
            DnsContainmentPolicy.Classify(new DnsState(["192.168.1.1"], [], false)));
    }

    [Fact]
    public void The_default_route_without_an_explicit_domain_is_contained()
    {
        Assert.True(ResolvectlOutputParser.TryParse("     Default Route: yes", out var state));
        Assert.Equal(DnsContainment.Contained, DnsContainmentPolicy.Classify(state));
    }

    [Fact]
    public void An_unparseable_or_empty_status_is_unknown()
    {
        Assert.False(ResolvectlOutputParser.TryParse("not a resolvectl output", out _));
        Assert.False(ResolvectlOutputParser.TryParse(string.Empty, out _));
        Assert.False(ResolvectlOutputParser.TryParse(null, out _));
    }

    [Fact]
    public async Task The_reader_runs_resolvectl_and_parses_its_output()
    {
        const string output = "       DNS Servers: 10.2.0.1\n        DNS Domain: ~.\n     Default Route: yes\n";
        var reader = new ResolvectlDnsStateReader(new FakeProcessRunner(_ => ProcessResult.Completed(0, output, string.Empty)));

        var result = await reader.ReadAsync("wg0");

        Assert.True(result.IsSuccess);
        Assert.Equal(DnsContainment.Contained, DnsContainmentPolicy.Classify(result.Value));
    }

    [Fact]
    public async Task The_reader_reports_a_bad_name_a_missing_tool_a_non_zero_exit_or_no_dns_line()
    {
        Assert.True((await new ResolvectlDnsStateReader(FakeProcessRunner.Exiting(0)).ReadAsync("bad name")).IsFailure);
        Assert.True((await new ResolvectlDnsStateReader(FakeProcessRunner.NotStarted()).ReadAsync("wg0")).IsFailure);
        Assert.True((await new ResolvectlDnsStateReader(FakeProcessRunner.Exiting(2, string.Empty, "boom")).ReadAsync("wg0")).IsFailure);
        Assert.True((await new ResolvectlDnsStateReader(FakeProcessRunner.Exiting(0, "no dns here", string.Empty)).ReadAsync("wg0")).IsFailure);
    }
}
