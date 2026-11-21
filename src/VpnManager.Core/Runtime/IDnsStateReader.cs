using Core.Results;

namespace VpnManager.Core.Runtime;

/// <summary>
/// Reads the resolver state of a link. It is read-only and rootless, and it never
/// changes the resolver: it only reports where the link resolves names.
/// </summary>
public interface IDnsStateReader
{
    /// <summary>Reads the link's resolver state.</summary>
    /// <param name="interfaceName">The interface, for example <c>wg0</c>.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The state, or a failure when it cannot be read.</returns>
    Task<Result<DnsState>> ReadAsync(string interfaceName, CancellationToken cancellationToken = default);
}
