using System.Globalization;
using System.Net;
using Core.Results;
using VpnManager.Core.Model;
using VpnManager.Core.Process;
using VpnManager.Core.Validation;

namespace VpnManager.Core.NetworkManager;

/// <summary>
/// The NetworkManager boundary over <c>nmcli</c>. It reads and toggles the
/// WireGuard connections of the workstation, which the product provisions there
/// instead of writing a <c>wg-quick</c> unit. No shell is involved: each verb is
/// an argument vector.
/// </summary>
public sealed class NmcliNetworkManagerConnections : INetworkManagerConnections
{
    private const string Executable = "nmcli";

    private readonly IProcessRunner _runner;

    // The detail call (permissions, interface name) has no bulk form in nmcli, so the
    // per-connection detail is cached; the app invalidates a name when it imports or
    // deletes it. A passive refresh therefore spawns no detail process after the
    // first one, instead of one per connection every poll.
    private readonly object _detailGate = new();
    private readonly Dictionary<string, NetworkManagerConnection> _detailCache = new(StringComparer.Ordinal);

    /// <summary>Initializes the boundary.</summary>
    /// <param name="runner">The process seam.</param>
    public NmcliNetworkManagerConnections(IProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<NetworkManagerConnection>>> ListWireGuardConnectionsAsync(CancellationToken cancellationToken = default)
    {
        var listed = await RunAsync(["-t", "-f", "NAME,TYPE,ACTIVE", "connection", "show"], cancellationToken).ConfigureAwait(false);
        if (!listed.TryGetValue(out var output))
        {
            return Result<IReadOnlyList<NetworkManagerConnection>>.Failure(listed.Error);
        }

        var entries = NetworkManagerConnectionParser.ParseConnectionList(output)
            .Where(entry => string.Equals(entry.Type, "wireguard", StringComparison.Ordinal))
            .Where(entry => IsSafeConnectionName(entry.Name))
            .ToArray();

        var connections = new List<NetworkManagerConnection>(entries.Length);
        var pending = new List<NetworkManagerConnectionEntry>();

        foreach (var entry in entries)
        {
            NetworkManagerConnection? cached;
            lock (_detailGate)
            {
                _detailCache.TryGetValue(entry.Name, out cached);
            }

            if (cached is not null)
            {
                // The interface and the permissions are cached; the active flag is not.
                connections.Add(cached with { IsActive = entry.IsActive });
            }
            else
            {
                pending.Add(entry);
            }
        }

        if (pending.Count > 0)
        {
            var details = await Task.WhenAll(pending.Select(entry => ReadDetailAsync(entry, cancellationToken))).ConfigureAwait(false);
            for (var index = 0; index < details.Length; index++)
            {
                if (!details[index].TryGetValue(out var connection))
                {
                    return Result<IReadOnlyList<NetworkManagerConnection>>.Failure(details[index].Error);
                }

                lock (_detailGate)
                {
                    _detailCache[connection.Name] = connection;
                }

                connections.Add(connection);
            }
        }

        return Result<IReadOnlyList<NetworkManagerConnection>>.Success(connections);
    }

    private async Task<Result<NetworkManagerConnection>> ReadDetailAsync(
        NetworkManagerConnectionEntry entry,
        CancellationToken cancellationToken)
    {
        if (!IsSafeConnectionName(entry.Name))
        {
            return Result<NetworkManagerConnection>.Failure(WireGuardErrors.NetworkManagerInvalidConnection(entry.Name));
        }

        var detail = await RunAsync(
            ["-t", "-f", "connection.permissions,connection.interface-name", "connection", "show", entry.Name],
            cancellationToken).ConfigureAwait(false);
        if (!detail.TryGetValue(out var detailOutput))
        {
            return Result<NetworkManagerConnection>.Failure(detail.Error);
        }

        var interfaceName = NetworkManagerConnectionParser.ReadProperty(detailOutput, "connection.interface-name") ?? entry.Name;
        var permissions = NetworkManagerConnectionParser.ReadPermissions(detailOutput);
        return Result<NetworkManagerConnection>.Success(new NetworkManagerConnection(entry.Name, interfaceName, entry.IsActive, permissions));
    }

    /// <inheritdoc />
    public async Task<Result<string>> EnsureAsync(
        string connectionName,
        string interfaceName,
        string configPath,
        string owner,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(interfaceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        if (!IsSafeConnectionName(connectionName) || !IsSafeConnectionName(interfaceName))
        {
            return Result<string>.Failure(WireGuardErrors.NetworkManagerInvalidConnection(connectionName));
        }

        Invalidate(connectionName);

        // Replace only a connection this account owns: import cannot overwrite, and
        // a foreign system connection must never be deleted by a user session.
        var listed = await ListWireGuardConnectionsAsync(cancellationToken).ConfigureAwait(false);
        if (!listed.TryGetValue(out var connections))
        {
            return Result<string>.Failure(listed.Error);
        }

        var existing = connections.FirstOrDefault(connection => string.Equals(connection.Name, connectionName, StringComparison.Ordinal));
        if (existing is not null && !existing.IsOwnedBy(owner))
        {
            return Result<string>.Failure(WireGuardErrors.NetworkManagerInvalidConnection(connectionName));
        }

        if (existing is not null)
        {
            var deleted = await DeleteAsync(connectionName, cancellationToken).ConfigureAwait(false);
            if (!deleted.IsSuccess)
            {
                return deleted;
            }
        }

        var imported = await RunAsync(["connection", "import", "type", "wireguard", "file", configPath], cancellationToken).ConfigureAwait(false);
        if (!imported.TryGetValue(out _))
        {
            return Result<string>.Failure(imported.Error);
        }

        // `import` names the connection after the file stem; rename it, make it the
        // owner's own manual connection, and pin the interface.
        var stem = Path.GetFileNameWithoutExtension(configPath);
        if (!IsSafeConnectionName(stem))
        {
            return Result<string>.Failure(WireGuardErrors.NetworkManagerInvalidConnection(stem));
        }

        var modified = await RunAsync(
            [
                "connection", "modify", stem,
                "connection.id", connectionName,
                "connection.interface-name", interfaceName,
                "connection.autoconnect", "no",
                "connection.permissions", string.Concat("user:", owner),
            ],
            cancellationToken).ConfigureAwait(false);

        return modified.TryGetValue(out _)
            ? Result<string>.Success(connectionName)
            : Result<string>.Failure(modified.Error);
    }

    /// <inheritdoc />
    public async Task<Result<string>> SetAutoConnectAsync(
        string connectionName,
        bool autoConnect,
        int priority,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);

        if (!IsSafeConnectionName(connectionName))
        {
            return Result<string>.Failure(WireGuardErrors.NetworkManagerInvalidConnection(connectionName));
        }

        if (priority is < -999 or > 999)
        {
            return Result<string>.Failure(WireGuardErrors.NetworkManagerInvalidPriority(priority));
        }

        Invalidate(connectionName);

        var arguments = new[]
        {
            "connection", "modify", connectionName,
            "connection.autoconnect", autoConnect ? "yes" : "no",
            "connection.autoconnect-priority", priority.ToString(CultureInfo.InvariantCulture),
        };

        var result = await RunAsync(arguments, cancellationToken).ConfigureAwait(false);
        return result.TryGetValue(out _)
            ? Result<string>.Success(connectionName)
            : Result<string>.Failure(result.Error);
    }

    /// <inheritdoc />
    public async Task<Result<string>> ApplyDnsAsync(
        string connectionName,
        IReadOnlyList<string> dnsEntries,
        IReadOnlyList<string> searchDomains,
        NetworkManagerDnsMode mode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);
        ArgumentNullException.ThrowIfNull(dnsEntries);
        ArgumentNullException.ThrowIfNull(searchDomains);

        if (!IsSafeConnectionName(connectionName))
        {
            return Result<string>.Failure(WireGuardErrors.NetworkManagerInvalidConnection(connectionName));
        }

        var ipv4 = new List<string>();
        var ipv6 = new List<string>();
        var search = new List<string>();

        foreach (var entry in dnsEntries)
        {
            if (!NetworkText.IsValidDnsEntry(entry))
            {
                return Result<string>.Failure(WireGuardErrors.Validation(
                    WireGuardErrorCodes.DnsInvalid,
                    $"DNS entry '{entry}' is not a usable address, host name or routing domain."));
            }

            if (IPAddress.TryParse(entry, out var address))
            {
                (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? ipv4 : ipv6).Add(entry);
            }
            else
            {
                search.Add(entry);
            }
        }

        foreach (var domain in searchDomains)
        {
            if (!NetworkText.IsValidDnsEntry(domain))
            {
                return Result<string>.Failure(WireGuardErrors.Validation(
                    WireGuardErrorCodes.DnsInvalid,
                    $"Search domain '{domain}' is not a usable host name or routing domain."));
            }

            search.Add(domain);
        }

        Invalidate(connectionName);

        var arguments = new List<string> { "connection", "modify", connectionName };

        if (mode == NetworkManagerDnsMode.System)
        {
            arguments.AddRange(
            [
                "ipv4.dns", string.Empty,
                "ipv6.dns", string.Empty,
                "ipv4.dns-search", string.Empty,
                "ipv6.dns-search", string.Empty,
                "ipv4.ignore-auto-dns", "no",
                "ipv6.ignore-auto-dns", "no",
                "ipv4.dns-priority", "0",
                "ipv6.dns-priority", "0",
            ]);
        }
        else
        {
            arguments.AddRange(
            [
                "ipv4.dns", string.Join(',', ipv4),
                "ipv6.dns", string.Join(',', ipv6),
                "ipv4.dns-search", string.Join(',', search.Distinct(StringComparer.OrdinalIgnoreCase)),
                "ipv6.dns-search", string.Join(',', search.Distinct(StringComparer.OrdinalIgnoreCase)),
                "ipv4.ignore-auto-dns", "yes",
                "ipv6.ignore-auto-dns", "yes",
                "ipv4.dns-priority", "-50",
                "ipv6.dns-priority", "-50",
            ]);
        }

        var result = await RunAsync(arguments, cancellationToken).ConfigureAwait(false);
        return result.TryGetValue(out _)
            ? Result<string>.Success(connectionName)
            : Result<string>.Failure(result.Error);
    }

    /// <inheritdoc />
    public Task<Result<string>> ActivateAsync(string connectionName, CancellationToken cancellationToken = default) =>
        ChangeStateAsync("up", connectionName, cancellationToken);

    /// <inheritdoc />
    public Task<Result<string>> DeactivateAsync(string connectionName, CancellationToken cancellationToken = default) =>
        ChangeStateAsync("down", connectionName, cancellationToken);

    /// <inheritdoc />
    public async Task<Result<string>> DeleteAsync(string connectionName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);

        if (!IsSafeConnectionName(connectionName))
        {
            return Result<string>.Failure(WireGuardErrors.NetworkManagerInvalidConnection(connectionName));
        }

        Invalidate(connectionName);

        var result = await RunAsync(["connection", "delete", connectionName], cancellationToken).ConfigureAwait(false);
        return result.TryGetValue(out _)
            ? Result<string>.Success(connectionName)
            : Result<string>.Failure(result.Error);
    }

    private static bool IsSafeConnectionName(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name[0] != '-' &&
        !name.Any(char.IsControl);

    private void Invalidate(string connectionName)
    {
        lock (_detailGate)
        {
            _detailCache.Remove(connectionName);
        }
    }

    private async Task<Result<string>> ChangeStateAsync(string verb, string connectionName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);

        if (!IsSafeConnectionName(connectionName))
        {
            return Result<string>.Failure(WireGuardErrors.NetworkManagerInvalidConnection(connectionName));
        }

        var result = await RunAsync(["connection", verb, connectionName], cancellationToken).ConfigureAwait(false);
        return result.TryGetValue(out _)
            ? Result<string>.Success(connectionName)
            : Result<string>.Failure(result.Error);
    }

    private async Task<Result<string>> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var request = new ProcessRequest(Executable, arguments);
        ProcessResult result;

        try
        {
            result = await _runner.RunAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result<string>.Failure(WireGuardErrors.NetworkManagerUnavailable());
        }

        if (!result.Started)
        {
            return Result<string>.Failure(WireGuardErrors.NetworkManagerUnavailable());
        }

        return result.ExitCode == 0
            ? Result<string>.Success(result.StandardOutput)
            : Result<string>.Failure(WireGuardErrors.NetworkManagerFailed(Describe(arguments), result.ExitCode, result.StandardError));
    }

    private static string Describe(IReadOnlyList<string> arguments) =>
        string.Join(' ', new[] { Executable }.Concat(arguments));
}
