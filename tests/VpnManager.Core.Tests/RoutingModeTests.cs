using VpnManager.Core.Configuration;
using VpnManager.Core.Model;

namespace VpnManager.Core.Tests;

/// <summary>
/// The full/split convergence: the same transformation the workstation's
/// <c>ensure_wg_mode_directives</c> applies, expressed as a pure function.
/// </summary>
public sealed class RoutingModeTests
{
    [Fact]
    public void A_default_route_peer_means_full_mode() =>
        Assert.Equal(TunnelRoutingMode.Full, RoutingModeConvergence.Detect(FullConfiguration()));

    [Fact]
    public void A_mesh_peer_means_split_mode() =>
        Assert.Equal(TunnelRoutingMode.Split, RoutingModeConvergence.Detect(TestData.ValidConfiguration()));

    [Fact]
    public void A_non_canonical_default_route_is_detected()
    {
        var configuration = TestData.ValidConfiguration() with
        {
            Peers = [TestData.ValidPeer() with { AllowedIPs = ["0.0.0.0/0,::/0"] }],
        };

        Assert.Equal(TunnelRoutingMode.Full, RoutingModeConvergence.Detect(configuration));
    }

    [Fact]
    public void Applying_full_pins_the_default_route_the_table_and_the_fwmark()
    {
        var result = RoutingModeConvergence.Apply(TestData.ValidConfiguration(), TunnelRoutingMode.Full);

        Assert.True(result.IsSuccess);
        Assert.Equal("auto", result.Value.Interface.Table);
        Assert.Equal("51820", result.Value.Interface.FwMark);
        Assert.Equal(["0.0.0.0/0", "::/0"], result.Value.Peers[0].AllowedIPs);
    }

    [Fact]
    public void Applying_full_without_a_peer_still_sets_the_interface_directives()
    {
        var configuration = TestData.ValidConfiguration() with { Peers = [] };

        var result = RoutingModeConvergence.Apply(configuration, TunnelRoutingMode.Full);

        Assert.True(result.IsSuccess);
        Assert.Equal("auto", result.Value.Interface.Table);
        Assert.Empty(result.Value.Peers);
    }

    [Fact]
    public void Applying_split_with_a_mesh_list_replaces_the_route_and_clears_the_directives()
    {
        var result = RoutingModeConvergence.Apply(FullConfiguration(), TunnelRoutingMode.Split, ["10.10.0.0/24"]);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Interface.Table);
        Assert.Null(result.Value.Interface.FwMark);
        Assert.Equal(["10.10.0.0/24"], result.Value.Peers[0].AllowedIPs);
    }

    [Fact]
    public void Applying_split_without_a_mesh_list_while_the_default_route_remains_is_refused()
    {
        var result = RoutingModeConvergence.Apply(FullConfiguration(), TunnelRoutingMode.Split);

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.RoutingModeSplitMissingAllowedIps, result.Error.Code);
    }

    [Fact]
    public void Applying_split_on_an_already_split_configuration_keeps_its_allowed_ips()
    {
        var result = RoutingModeConvergence.Apply(TestData.ValidConfiguration(), TunnelRoutingMode.Split);

        Assert.True(result.IsSuccess);
        Assert.Equal(["10.10.0.0/24"], result.Value.Peers[0].AllowedIPs);
    }

    [Fact]
    public void The_second_peer_is_preserved_when_the_first_becomes_full()
    {
        var second = TestData.ValidPeer() with { PublicKey = TestData.PeerTwoKey, AllowedIPs = ["10.20.0.0/24"] };
        var configuration = TestData.ValidConfiguration() with { Peers = [TestData.ValidPeer(), second] };

        var result = RoutingModeConvergence.Apply(configuration, TunnelRoutingMode.Full);

        Assert.True(result.IsSuccess);
        Assert.Equal(["0.0.0.0/0", "::/0"], result.Value.Peers[0].AllowedIPs);
        Assert.Equal(["10.20.0.0/24"], result.Value.Peers[1].AllowedIPs);
    }

    [Fact]
    public void The_written_full_configuration_carries_the_deployment_directives()
    {
        var converged = RoutingModeConvergence.Apply(TestData.ValidConfiguration(), TunnelRoutingMode.Full).Value;

        var text = WireGuardConfigWriter.Write(converged);

        Assert.Contains("Table = auto", text, StringComparison.Ordinal);
        Assert.Contains("FwMark = 51820", text, StringComparison.Ordinal);
        Assert.Contains("AllowedIPs = 0.0.0.0/0", text, StringComparison.Ordinal);
        Assert.Contains("AllowedIPs = ::/0", text, StringComparison.Ordinal);
    }

    private static TunnelConfiguration FullConfiguration() =>
        RoutingModeConvergence.Apply(TestData.ValidConfiguration(), TunnelRoutingMode.Full).Value;
}
