using Core.Results;

namespace VpnManager.Helper;

/// <summary>
/// The fixed verbs the privileged helper accepts. It never takes an executable,
/// a path or a command fragment from the caller.
/// </summary>
internal enum HelperVerb
{
    None = 0,
    SysctlSrcValidMark = 1,
    SelfTest = 2,
}

internal readonly record struct HelperCommand(HelperVerb Verb, string? Error)
{
    internal static HelperCommand Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args is ["--self-test"])
        {
            return new HelperCommand(HelperVerb.SelfTest, null);
        }

        if (args is ["sysctl-src-valid-mark"])
        {
            return new HelperCommand(HelperVerb.SysctlSrcValidMark, null);
        }

        if (args.Length == 0 || args is ["--help"] or ["-h"])
        {
            return new HelperCommand(HelperVerb.None, null);
        }

        return new HelperCommand(HelperVerb.None, "Unknown verb; the helper accepts only sysctl-src-valid-mark.");
    }
}

/// <summary>
/// The one privileged operation of 0.1.0: set and verify the full-tunnel
/// reverse-path sysctl. The path is fixed; no caller-supplied path or command is
/// accepted.
/// </summary>
internal static class SrcValidMark
{
    internal const string Path = "/proc/sys/net/ipv4/conf/all/src_valid_mark";
    internal const string Expected = "1";

    internal static Result<string> Apply()
    {
        try
        {
            File.WriteAllText(Path, Expected);
            var readBack = File.ReadAllText(Path).Trim();
            return readBack == Expected
                ? Result<string>.Success("src_valid_mark=1")
                : Result<string>.Failure(new CoreError(
                    "helper.sysctl_unverified",
                    $"The kernel reported src_valid_mark='{readBack}' after the write."));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result<string>.Failure(new CoreError(
                "helper.sysctl_failed",
                "The sysctl could not be written; the helper must run as root."));
        }
    }
}

internal static class HelperSelfTest
{
    internal static int Run(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var failures = 0;
        Check(output, "the verb is parsed", HelperCommand.Parse(["sysctl-src-valid-mark"]).Verb == HelperVerb.SysctlSrcValidMark, ref failures);
        Check(output, "an unknown verb is refused", HelperCommand.Parse(["whoami"]).Error is not null, ref failures);
        Check(output, "the sysctl path is fixed", SrcValidMark.Path == "/proc/sys/net/ipv4/conf/all/src_valid_mark", ref failures);

        if (failures == 0)
        {
            output.WriteLine("SELF-TEST PASS");
            return 0;
        }

        return 1;
    }

    private static void Check(TextWriter output, string name, bool passed, ref int failures)
    {
        if (!passed)
        {
            output.WriteLine($"SELF-TEST FAIL: {name}");
            failures++;
        }
    }
}
