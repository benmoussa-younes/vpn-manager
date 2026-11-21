using Core.Results;
using VpnManager.Core.NetworkManager;

namespace VpnManager.Core.Tests.Fakes;

/// <summary>
/// A NetworkManager boundary whose connections and outcome are chosen by the
/// test, so the integration is exercised without a NetworkManager on the machine.
/// </summary>
internal sealed class FakeNetworkManagerConnections : INetworkManagerConnections
{
    /// <summary>The connections <c>nmcli</c> would report.</summary>
    internal List<NetworkManagerConnection> Connections { get; } = [];

    /// <summary>The connection ids activated, in order.</summary>
    internal List<string> Activated { get; } = [];

    /// <summary>The connection ids deactivated, in order.</summary>
    internal List<string> Deactivated { get; } = [];

    /// <summary>The connection ids ensured (imported), in order.</summary>
    internal List<string> Ensured { get; } = [];

    /// <summary>The connection ids deleted, in order.</summary>
    internal List<string> Deleted { get; } = [];

    /// <summary>The autoconnect changes, in order.</summary>
    internal List<(string Name, bool AutoConnect, int Priority)> AutoConnectChanges { get; } = [];

    /// <summary>The DNS changes, in order.</summary>
    internal List<(string Name, IReadOnlyList<string> Dns, IReadOnlyList<string> Search, NetworkManagerDnsMode Mode)> DnsChanges { get; } = [];

    /// <summary>When set, the policy calls fail with this error.</summary>
    internal CoreError? PolicyError { get; set; }

    /// <summary>When set, the listing fails with this error.</summary>
    internal CoreError? ListError { get; set; }

    /// <summary>When set, activation and deactivation fail with this error.</summary>
    internal CoreError? ChangeError { get; set; }

    /// <summary>When set, ensuring a connection fails with this error.</summary>
    internal CoreError? EnsureError { get; set; }

    public Task<Result<IReadOnlyList<NetworkManagerConnection>>> ListWireGuardConnectionsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(ListError is null
            ? Result<IReadOnlyList<NetworkManagerConnection>>.Success(Connections.ToArray())
            : Result<IReadOnlyList<NetworkManagerConnection>>.Failure(ListError));

    public Task<Result<string>> EnsureAsync(
        string connectionName,
        string interfaceName,
        string configPath,
        string owner,
        CancellationToken cancellationToken = default)
    {
        Ensured.Add(connectionName);
        return Task.FromResult(EnsureError is null
            ? Result<string>.Success(connectionName)
            : Result<string>.Failure(EnsureError));
    }

    public Task<Result<string>> SetAutoConnectAsync(
        string connectionName,
        bool autoConnect,
        int priority,
        CancellationToken cancellationToken = default)
    {
        AutoConnectChanges.Add((connectionName, autoConnect, priority));
        return Task.FromResult(PolicyError is null
            ? Result<string>.Success(connectionName)
            : Result<string>.Failure(PolicyError));
    }

    public Task<Result<string>> ApplyDnsAsync(
        string connectionName,
        IReadOnlyList<string> dnsEntries,
        IReadOnlyList<string> searchDomains,
        NetworkManagerDnsMode mode,
        CancellationToken cancellationToken = default)
    {
        DnsChanges.Add((connectionName, dnsEntries, searchDomains, mode));
        return Task.FromResult(PolicyError is null
            ? Result<string>.Success(connectionName)
            : Result<string>.Failure(PolicyError));
    }

    public Task<Result<string>> ActivateAsync(string connectionName, CancellationToken cancellationToken = default)
    {
        Activated.Add(connectionName);
        return Task.FromResult(ChangeError is null
            ? Result<string>.Success(connectionName)
            : Result<string>.Failure(ChangeError));
    }

    public Task<Result<string>> DeactivateAsync(string connectionName, CancellationToken cancellationToken = default)
    {
        Deactivated.Add(connectionName);
        return Task.FromResult(ChangeError is null
            ? Result<string>.Success(connectionName)
            : Result<string>.Failure(ChangeError));
    }

    public Task<Result<string>> DeleteAsync(string connectionName, CancellationToken cancellationToken = default)
    {
        Deleted.Add(connectionName);
        return Task.FromResult(ChangeError is null
            ? Result<string>.Success(connectionName)
            : Result<string>.Failure(ChangeError));
    }
}
