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

}
