using System.Globalization;
using VpnManager.Core.Runtime;
using VpnManager.Core.Tests.Fakes;

namespace VpnManager.Core.Tests;

/// <summary>
/// The full-tunnel prerequisite: a strict reverse-path filter without the packet
/// mark is the setting that black-holes a tunnel to a remote endpoint. The reader is
/// read-only, so the application can only detect and warn.
/// </summary>
public sealed class RoutingPrerequisiteTests
{
    private const string SrcPath = "/proc/sys/net/ipv4/conf/all/src_valid_mark";
    private const string RpPath = "/proc/sys/net/ipv4/conf/all/rp_filter";

    [Fact]
    public void A_strict_filter_without_the_mark_is_unsatisfied()
    {
        var state = Reader(0, 1).Read();

        Assert.NotNull(state);
        Assert.False(state!.IsSatisfied);
        Assert.Contains("src_valid_mark=1", state.Remediation, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    public void A_mark_or_a_disabled_filter_is_satisfied(int src, int rp) =>
        Assert.True(Reader(src, rp).Read()!.IsSatisfied);

    [Fact]
    public void Unreadable_files_are_unknown() =>
        Assert.Null(new ProcRoutingPrerequisiteReader(new FakeConfigFileSystem()).Read());

    private static ProcRoutingPrerequisiteReader Reader(int src, int rp)
    {
        var fileSystem = new FakeConfigFileSystem();
        fileSystem.Files[SrcPath] = src.ToString(CultureInfo.InvariantCulture);
        fileSystem.Files[RpPath] = rp.ToString(CultureInfo.InvariantCulture);
        return new ProcRoutingPrerequisiteReader(fileSystem);
    }
}
