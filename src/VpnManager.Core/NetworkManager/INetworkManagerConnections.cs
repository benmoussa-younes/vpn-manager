using Core.Results;

namespace VpnManager.Core.NetworkManager;

/// <summary>
/// The NetworkManager boundary: the WireGuard connections it owns, their import
/// and their activation. It is a seam because NetworkManager is the machine's
/// connection engine, so the application manages the profile there instead of
/// touching <c>/etc/wireguard</c>.
/// </summary>
public interface INetworkManagerConnections
{
    /// <summary>Lists every NetworkManager connection of type WireGuard.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The connections, or a failure when <c>nmcli</c> could not run.</returns>
    Task<Result<IReadOnlyList<NetworkManagerConnection>>> ListWireGuardConnectionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports (or refreshes) a WireGuard connection from a configuration file and
    /// makes it the owner's own, manual connection. Any existing connection with the
    /// same id is replaced.
    /// </summary>
    /// <param name="connectionName">The connection id to create or replace.</param>
    /// <param name="interfaceName">The interface the connection creates.</param>
    /// <param name="configPath">The wg-quick configuration file to import.</param>
    /// <param name="owner">The account the connection belongs to.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The connection id, or a failure.</returns>
    Task<Result<string>> EnsureAsync(
        string connectionName,
        string interfaceName,
        string configPath,
        string owner,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets whether NetworkManager may reconnect the connection and its priority.
    /// </summary>
    /// <param name="connectionName">The connection id.</param>
    /// <param name="autoConnect">Whether the connection may reconnect automatically.</param>
    /// <param name="priority">The autoconnect priority; a higher value wins.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The connection id, or a failure.</returns>
    Task<Result<string>> SetAutoConnectAsync(
        string connectionName,
        bool autoConnect,
        int priority,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the profile DNS entries and the policy to the connection.
    /// </summary>
    /// <param name="connectionName">The connection id.</param>
    /// <param name="dnsEntries">The profile DNS entries (addresses, host names or routing domains).</param>
    /// <param name="searchDomains">The search domains for split DNS.</param>
    /// <param name="mode">The DNS mode.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The connection id, or a failure.</returns>
    Task<Result<string>> ApplyDnsAsync(
        string connectionName,
        IReadOnlyList<string> dnsEntries,
        IReadOnlyList<string> searchDomains,
        NetworkManagerDnsMode mode,
        bool includeIpv6 = true,
        CancellationToken cancellationToken = default);

    /// <summary>Brings a connection up through NetworkManager.</summary>
    /// <param name="connectionName">The connection id.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The connection id, or a failure.</returns>
    /// <summary>Renames a connection id without touching its interface.</summary>
    /// <param name="connectionName">The current connection id.</param>
    /// <param name="newConnectionName">The new connection id.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The new id, or a failure.</returns>
    Task<Result<string>> SetConnectionIdAsync(
        string connectionName,
        string newConnectionName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reapplies the IP configuration of an active device, so a profile change
    /// takes effect without dropping the connection. <c>nmcli connection modify</c>
    /// only updates the stored profile; the active device keeps the previous DNS
    /// until it reapplies.
    /// </summary>
    /// <param name="deviceName">The kernel interface of the active connection.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The device name, or a failure.</returns>
    Task<Result<string>> ReapplyAsync(string deviceName, CancellationToken cancellationToken = default);

    Task<Result<string>> ActivateAsync(string connectionName, CancellationToken cancellationToken = default);

    /// <summary>Takes a connection down through NetworkManager.</summary>
    /// <param name="connectionName">The connection id.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The connection id, or a failure.</returns>
    Task<Result<string>> DeactivateAsync(string connectionName, CancellationToken cancellationToken = default);

    /// <summary>Deletes a connection.</summary>
    /// <param name="connectionName">The connection id.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The connection id, or a failure.</returns>
    Task<Result<string>> DeleteAsync(string connectionName, CancellationToken cancellationToken = default);
}
