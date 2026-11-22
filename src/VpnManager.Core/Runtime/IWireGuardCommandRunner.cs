using Core.Results;

namespace VpnManager.Core.Runtime;

/// <summary>
/// The boundary every WireGuard tool invocation crosses. The application layer
/// depends on this, never on <see cref="System.Diagnostics.Process"/>; tests
/// substitute a fake.
/// </summary>
public interface IWireGuardCommandRunner
{
    /// <summary>
    /// Runs one command and returns its output, or a failure value describing
    /// why it could not run or exited non-zero.
    /// </summary>
    /// <param name="command">The semantic command.</param>
    /// <param name="cancellationToken">Cancels the wait for the process.</param>
    /// <returns>The captured output, or a failure.</returns>
    Task<Result<CommandOutput>> RunAsync(WireGuardCommand command, CancellationToken cancellationToken = default);
}
