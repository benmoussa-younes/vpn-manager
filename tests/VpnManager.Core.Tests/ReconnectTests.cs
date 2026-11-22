using VpnManager.Core.Presentation;
using VpnManager.Core.Runtime;
using VpnManager.Core.Tests.Fakes;

namespace VpnManager.Core.Tests;

/// <summary>
/// The rootless reconnect signal: the physical default route read from
/// <c>/proc/net/route</c>, and the pure decision that turns a change into a
/// reconnect. The detector never reads <c>wg</c>, which needs root.
/// </summary>
public sealed class ReconnectTests
{
    [Fact]
    public void The_default_route_is_read_and_its_gateway_formatted()
    {
        const string route = """
            Iface	Destination	Gateway 	Flags	RefCnt	Use	Metric	Mask		MTU	Window	IRTT
            wlp0s20f3	00000000	0101A8C0	0003	0	0	600	00000000	0	0	0
            wlp0s20f3	0001A8C0	00000000	0001	0	0	600	00FFFFFF	0	0	0
            """;

        Assert.True(ProcNetRouteParser.TryReadDefaultRoute(route, out var link));
        Assert.Equal("wlp0s20f3", link.Interface);
        Assert.Equal("192.168.1.1", link.Gateway);
        Assert.Equal("wlp0s20f3/192.168.1.1", link.Key);
    }

    [Fact]
    public void The_lowest_metric_default_route_wins()
    {
        const string route = """
            Iface	Destination	Gateway 	Flags	RefCnt	Use	Metric	Mask		MTU	Window	IRTT
            wlan0	00000000	0101A8C1	0003	0	0	600	00000000	0	0	0
            eth0	00000000	0101A8C0	0003	0	0	100	00000000	0	0	0
            """;

        Assert.True(ProcNetRouteParser.TryReadDefaultRoute(route, out var link));
        Assert.Equal("eth0", link.Interface);
        Assert.Equal("192.168.1.1", link.Gateway);
    }

    [Fact]
    public void An_on_link_default_or_no_default_is_not_a_link()
    {
        const string onLink = """
            Iface	Destination	Gateway 	Flags	RefCnt	Use	Metric	Mask		MTU	Window	IRTT
            wg0	00000000	00000000	0001	0	0	50	00000000	0	0	0
            """;

        Assert.False(ProcNetRouteParser.TryReadDefaultRoute(onLink, out _));
        Assert.False(ProcNetRouteParser.TryReadDefaultRoute("Iface\tDestination\n", out _));
        Assert.False(ProcNetRouteParser.TryReadDefaultRoute(null, out _));
        Assert.False(ProcNetRouteParser.TryReadDefaultRoute(string.Empty, out _));
    }

    [Theory]
    [InlineData(null, "wlan0/192.168.1.1", true, true)]
    [InlineData("wlan0/192.168.1.1", "wlan0/192.168.1.1", true, false)]
    [InlineData("wlan0/192.168.1.1", "wlan0/192.168.1.254", true, true)]
    [InlineData("wlan0/192.168.1.1", "wlan0/192.168.1.1", false, false)]
    [InlineData("wlan0/192.168.1.1", null, true, false)]
    [InlineData(null, null, true, false)]
    public void The_policy_reconnects_only_on_a_real_change_while_an_owned_tunnel_is_up(
        string? previous,
        string? current,
        bool ownedTunnelUp,
        bool expected)
    {
        Assert.Equal(expected, ReconnectPolicy.ShouldReconnect(previous, current, ownedTunnelUp));
    }

    [Fact]
    public void The_reader_reads_the_default_route_file_and_handles_a_missing_one()
    {
        var fileSystem = new FakeConfigFileSystem();
        fileSystem.Files["/proc/net/route"] =
            "Iface\tDestination\tGateway \tFlags\tRefCnt\tUse\tMetric\tMask\t\tMTU\tWindow\tIRTT\n" +
            "wlan0\t00000000\t0101A8C0\t0003\t0\t0\t600\t00000000\t0\t0\t0\n";
        var reader = new ProcNetRoutePhysicalLinkReader(fileSystem);

        var link = reader.Read();

        Assert.NotNull(link);
        Assert.Equal("wlan0/192.168.1.1", link!.Key);
        Assert.Null(new ProcNetRoutePhysicalLinkReader(new FakeConfigFileSystem()).Read());
    }
}
