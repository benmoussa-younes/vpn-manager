namespace VpnManager.Core.Runtime;

/// <summary>
/// The captured result of a successful external command: a zero exit code and
/// its two streams.
/// </summary>
/// <param name="ExitCode">The process exit code; always zero for a success.</param>
/// <param name="StandardOutput">Everything the process wrote to standard output.</param>
/// <param name="StandardError">Everything the process wrote to standard error.</param>
public sealed record CommandOutput(int ExitCode, string StandardOutput, string StandardError);
