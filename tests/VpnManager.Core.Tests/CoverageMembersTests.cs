using VpnManager.Core.Model;
using VpnManager.Core.Presentation;
using VpnManager.Core.Services;
using VpnManager.Core.State;
using VpnManager.Core.Tests.Fakes;
using VpnManager.Core.Validation;

namespace VpnManager.Core.Tests;

/// <summary>
/// A few shipped members the main suites leave: the name rule, the directory
/// creation, the state value and the editor snapshot. Each is asserted rather than
/// excluded from the coverage gate.
/// </summary>
public sealed class CoverageMembersTests
{
    [Theory]
    [InlineData("wg0", true)]
    [InlineData("work-vpn", true)]
    [InlineData("a..b", false)]
    [InlineData("..", false)]
    [InlineData("a b", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_file_stem_is_confined_only_when_it_is_valid_and_carries_no_traversal(string? name, bool expected)
    {
        Assert.Equal(expected, TunnelNameRules.IsConfined(name));
    }

    [Fact]
    public void The_file_system_creates_a_directory_and_its_parents()
    {
        using var directory = new TemporaryDirectory();
        var fileSystem = TestFileSystems.SystemConfig();
        var nested = Path.Combine(directory.Path, "benmoussa", "vpn-manager");

        Assert.True(fileSystem.CreateDirectory(nested));
        Assert.True(fileSystem.DirectoryExists(nested));
    }

    [Fact]
    public void The_state_exposes_the_last_scope()
    {
        var state = new ApplicationState { LastScope = TunnelScope.User };

        Assert.Equal(TunnelScope.User, state.LastScope);
    }

    [Fact]
    public void The_presenter_session_exposes_its_service()
    {
        var service = new TunnelService(
            new FakeWireGuardConfigStore(),
            new FakeWireGuardRuntimeInspector());
        var session = new PresenterSession(service);

        Assert.Same(service, session.Service);
    }

    [Fact]
    public void The_editor_snapshot_exposes_its_routing_mode()
    {
        var snapshot = new EditorSnapshot(
            "wg0", false, "wg0", false, "10.0.0.1/32", string.Empty, string.Empty, string.Empty, string.Empty,
            TunnelRoutingMode.Full, true, "cHVibGlj", []);

        Assert.Equal(TunnelRoutingMode.Full, snapshot.RoutingMode);
    }
}
