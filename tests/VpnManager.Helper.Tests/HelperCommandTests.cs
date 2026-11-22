namespace VpnManager.Helper.Tests;

public sealed class HelperCommandTests
{
    [Fact]
    public void The_only_privileged_verb_is_the_fixed_sysctl()
    {
        var command = HelperCommand.Parse(["sysctl-src-valid-mark"]);

        Assert.Equal(HelperVerb.SysctlSrcValidMark, command.Verb);
        Assert.Null(command.Error);
    }

    [Theory]
    [InlineData("whoami")]
    [InlineData("/bin/sh")]
    [InlineData("--evil")]
    [InlineData("sysctl-src-valid-mark extra")]
    public void An_arbitrary_or_extra_argument_is_refused(string raw)
    {
        var command = HelperCommand.Parse(raw.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        Assert.Equal(HelperVerb.None, command.Verb);
        Assert.NotNull(command.Error);
    }

    [Fact]
    public void The_self_test_passes()
    {
        using var output = new StringWriter();

        var code = HelperSelfTest.Run(output);

        Assert.Equal(0, code);
        Assert.Contains("SELF-TEST PASS", output.ToString(), StringComparison.Ordinal);
    }
}
