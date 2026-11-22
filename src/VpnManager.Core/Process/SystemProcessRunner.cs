using CoreProcessRequest = Core.Execution.ProcessRequest;

namespace VpnManager.Core.Process;

/// <summary>
/// The real process runner over Core's Linux adapter. The product keeps its
/// semantic request and result types, while Core supplies the trusted-directory
/// resolution, the argument-vector start, the output cap, the timeout and the
/// redacted description.
/// </summary>
public sealed class SystemProcessRunner : IProcessRunner
{
    private readonly global::Core.Platform.Linux.SystemProcessRunner _inner = new();

    /// <inheritdoc />
    public async Task<ProcessResult> RunAsync(
        ProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var run = await _inner.RunAsync(
            new CoreProcessRequest(
                request.FileName,
                request.Arguments,
                request.StandardInput,
                request.Environment),
            cancellationToken).ConfigureAwait(false);

        if (run.TryGetError(out var error))
        {
            return ProcessResult.NotStarted(error.Message);
        }

        var completed = run.Value;
        return ProcessResult.Completed(completed.ExitCode, completed.StandardOutput, completed.StandardError);
    }
}
