namespace VpnManager.Core.Process;

/// <summary>
/// One process invocation: an executable and its already-split arguments. The
/// runner never builds a command line string, so no shell is involved and no
/// argument can be re-parsed.
/// </summary>
/// <param name="FileName">The executable to start.</param>
/// <param name="Arguments">The arguments, passed as a vector.</param>
/// <param name="StandardInput">Text to write to the process standard input, or <see langword="null"/>.</param>
/// <param name="Environment">Extra environment variables for the child, or <see langword="null"/>.</param>
public sealed record ProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? StandardInput = null,
    IReadOnlyDictionary<string, string>? Environment = null);

/// <summary>
/// The result of starting and waiting for a process, including the case where
/// the executable could not be started at all.
/// </summary>
/// <param name="Started">Whether the operating system started the process.</param>
/// <param name="ExitCode">The exit code, or -1 when it never started.</param>
/// <param name="StandardOutput">The captured standard output.</param>
/// <param name="StandardError">The captured standard error, or the start failure message.</param>
public sealed record ProcessResult(bool Started, int ExitCode, string StandardOutput, string StandardError)
{
    /// <summary>Records a process that could not be started.</summary>
    /// <param name="message">Why it could not start.</param>
    /// <returns>The result.</returns>
    public static ProcessResult NotStarted(string message) => new(false, -1, string.Empty, message);

    /// <summary>Records a process that ran to completion.</summary>
    /// <param name="exitCode">Its exit code.</param>
    /// <param name="standardOutput">Its standard output.</param>
    /// <param name="standardError">Its standard error.</param>
    /// <returns>The result.</returns>
    public static ProcessResult Completed(int exitCode, string standardOutput, string standardError) =>
        new(true, exitCode, standardOutput, standardError);
}

/// <summary>
/// Starts a process and captures its streams. This is the only seam between the
/// application and the operating system's process API; tests substitute a fake,
/// which is how the privileged paths are exercised without privileges.
/// </summary>
public interface IProcessRunner
{
    /// <summary>Runs one process to completion.</summary>
    /// <param name="request">The executable, its arguments and its optional input.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The captured result.</returns>
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default);
}
