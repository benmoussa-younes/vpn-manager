using Core.Results;
using VpnManager.Core.Model;
using VpnManager.Core.Process;

namespace VpnManager.App.Privileges;

/// <summary>The narrow privileged actions the product may ask for.</summary>
internal interface IRootActionService
{
    /// <summary>Gets whether the helper and its polkit launcher are installed.</summary>
    bool IsAvailable { get; }

    /// <summary>Sets the full-tunnel reverse-path sysctl through the root helper.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The applied value, or a typed failure.</returns>
    Task<Result<string>> SetSrcValidMarkAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The 0.1.0 root-action path: <c>pkexec</c> launches the fixed helper, and the
/// polkit action <c>com.bit-crafts.vpn-manager.sysctl-src-valid-mark</c> asks the
/// operator. The helper path and the verb are fixed; no caller data reaches the
/// command line.
/// </summary>
internal sealed class PkexecRootActionService : IRootActionService
{
    internal const string HelperPath = "/usr/lib/vpn-manager/vpn-manager-helper";
    internal const string PkexecPath = "/usr/bin/pkexec";
    internal const string Verb = "sysctl-src-valid-mark";

    private readonly IProcessRunner _runner;

    internal PkexecRootActionService(IProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    /// <inheritdoc />
    public bool IsAvailable => File.Exists(HelperPath) && File.Exists(PkexecPath);

    /// <inheritdoc />
    public async Task<Result<string>> SetSrcValidMarkAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return Result<string>.Failure(new CoreError(
                "privilege.helper_unavailable",
                "The VPN Manager helper or pkexec is not installed."));
        }

        var result = await _runner.RunAsync(BuildRequest(), cancellationToken).ConfigureAwait(false);
        if (!result.Started)
        {
            return Result<string>.Failure(WireGuardErrors.ToolsUnavailable("pkexec"));
        }

        if (result.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardError)
                ? "polkit refused or the helper failed"
                : result.StandardError.Trim();
            return Result<string>.Failure(new CoreError("privilege.refused", detail));
        }

        return Result<string>.Success("src_valid_mark=1");
    }

    /// <summary>Builds the fixed request, for the test that proves no caller data is used.</summary>
    /// <returns>The process request.</returns>
    internal static ProcessRequest BuildRequest() => new(PkexecPath, [HelperPath, Verb]);
}
