using Core.Results;
using VpnManager.Core.Model;
using VpnManager.Core.Process;

namespace VpnManager.Core.Runtime;

/// <summary>
/// Maps a semantic <see cref="WireGuardCommand"/> onto the process runner and
/// the process result onto a failure value the application understands.
/// </summary>
/// <remarks>
/// This type is where the "no privilege escalation" rule is enforced in the
/// application layer: it never adds an argument, never wraps the executable in a
/// shell, and it never asks for elevation. The privileged paths (<c>wg show</c>
/// with peers, <c>wg-quick up|down</c>) are therefore exercised with a fake
/// process runner in tests and reported unverified on this unprivileged machine.
/// </remarks>
public sealed class WireGuardCommandRunner : IWireGuardCommandRunner
{
    private readonly IProcessRunner _processRunner;

    /// <summary>Initializes the runner over a process boundary.</summary>
    /// <param name="processRunner">The process seam.</param>
    public WireGuardCommandRunner(IProcessRunner processRunner)
    {
        ArgumentNullException.ThrowIfNull(processRunner);
        _processRunner = processRunner;
    }

    /// <inheritdoc />
    public async Task<Result<CommandOutput>> RunAsync(WireGuardCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = new ProcessRequest(command.Executable, command.Arguments, command.StandardInput);
        var result = await _processRunner.RunAsync(request, cancellationToken).ConfigureAwait(false);

        if (!result.Started)
        {
            return Result<CommandOutput>.Failure(WireGuardErrors.ToolsUnavailable(command.Executable));
        }

        if (result.ExitCode != 0)
        {
            return Result<CommandOutput>.Failure(MapFailure(command, result));
        }

        return Result<CommandOutput>.Success(new CommandOutput(result.ExitCode, result.StandardOutput, result.StandardError));
    }

    /// <summary>
    /// Turns a non-zero exit into the most specific failure the error text
    /// supports: a privilege refusal, a missing interface, or a generic command
    /// failure.
    /// </summary>
    /// <param name="command">The command that failed.</param>
    /// <param name="result">Its captured result.</param>
    /// <returns>The failure.</returns>
    internal static CoreError MapFailure(WireGuardCommand command, ProcessResult result)
    {
        var text = result.StandardError;

        if (Contains(text, "operation not permitted") ||
            Contains(text, "permission denied") ||
            Contains(text, "not permitted"))
        {
            return WireGuardErrors.CommandPermissionDenied(command.ToString(), text);
        }

        if (Contains(text, "no such device") || Contains(text, "does not exist"))
        {
            return WireGuardErrors.InterfaceNotFound(command.ToString());
        }

        return WireGuardErrors.CommandFailed(command.ToString(), result.ExitCode, text);
    }

    private static bool Contains(string text, string value) => text.Contains(value, StringComparison.OrdinalIgnoreCase);
}
