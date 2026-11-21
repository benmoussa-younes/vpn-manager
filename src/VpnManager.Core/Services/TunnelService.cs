using System.Net;
using Core.Results;
using VpnManager.Core.Configuration;
using VpnManager.Core.Model;
using VpnManager.Core.NetworkManager;
using VpnManager.Core.Runtime;
using VpnManager.Core.State;
using VpnManager.Core.Storage;
using VpnManager.Core.Validation;

namespace VpnManager.Core.Services;

/// <summary>
/// The application operations over WireGuard: list, read, create, edit, delete,
/// start, stop, import, export and key generation. Every effect crosses an
/// interface (<see cref="IWireGuardConfigStore"/>,
/// <see cref="IWireGuardRuntimeInspector"/>),
/// so the whole service is exercised with fakes and no privilege.
/// </summary>
/// <remarks>
/// The service keeps the application's memory (<see cref="IApplicationStateStore"/>)
/// in step with the truth: the system configuration directory is root-only, so
/// without this index a configured tunnel would vanish from the list while down.
/// </remarks>
public sealed class TunnelService
{
    private readonly IWireGuardConfigStore _store;
    private readonly IWireGuardRuntimeInspector _runtime;
    private readonly TunnelScope _scope;
    private readonly IApplicationStateStore? _stateStore;
    private readonly INetworkManagerConnections? _networkManager;
    private readonly string? _networkManagerOwner;
    private readonly IDnsStateReader? _dnsStateReader;
    private readonly IConfigFileSystem? _fileSystem;
    private readonly string _importDirectory;
    private readonly bool _readOnly;
    private HashSet<string> _networkManagerNames = new(StringComparer.Ordinal);
    private Dictionary<string, string> _networkManagerInterfaces = new(StringComparer.Ordinal);

    /// <summary>Initializes the service.</summary>
    /// <param name="store">The configuration store.</param>
    /// <param name="runtime">The runtime reader.</param>
    /// <param name="scope">The scope this service manages.</param>
    /// <param name="stateStore">The application memory, or <see langword="null"/> when there is none.</param>
    /// <param name="networkManager">The NetworkManager boundary, or <see langword="null"/> when the machine has none.</param>
    /// <param name="networkManagerOwner">When set, only the NetworkManager connections this account owns are shown (the user scope).</param>
    /// <param name="readOnly">When set, every mutating operation is refused.</param>
    /// <param name="dnsStateReader">The resolver-state reader, or <see langword="null"/> when there is none.</param>
    /// <param name="fileSystem">The file-system seam, used to read the host's IPv6 state, or <see langword="null"/>.</param>
    /// <param name="importDirectory">
    /// The owner-only directory the temporary NetworkManager profile is written
    /// under; a null value uses the per-user runtime directory, or the
    /// application state directory when the host supplies neither.
    /// </param>
    public TunnelService(
        IWireGuardConfigStore store,
        IWireGuardRuntimeInspector runtime,
        TunnelScope scope = TunnelScope.System,
        IApplicationStateStore? stateStore = null,
        INetworkManagerConnections? networkManager = null,
        string? networkManagerOwner = null,
        bool readOnly = false,
        IDnsStateReader? dnsStateReader = null,
        IConfigFileSystem? fileSystem = null,
        string? importDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(runtime);

        _store = store;
        _runtime = runtime;
        _scope = scope;
        _stateStore = stateStore;
        _networkManager = networkManager;
        _networkManagerOwner = networkManagerOwner;
        _readOnly = readOnly;
        _dnsStateReader = dnsStateReader;
        _fileSystem = fileSystem;
        _importDirectory = importDirectory
            ?? Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")
            ?? Path.Combine(Path.GetTempPath(), string.Concat("vpn-manager-", Environment.UserName));
    }

    /// <summary>Gets the directory the configuration files live in.</summary>
    public string ConfigDirectory => _store.ConfigDirectory;

    /// <summary>
    /// Lists the tunnels the machine knows about: the configured names merged
    /// with the interfaces the kernel reports.
    /// </summary>
    /// <param name="reconcile">
    /// When <see langword="true"/> and the directory is unreadable, ask the elevated
    /// listing (which prompts once) to confirm which tunnels exist, then prune the
    /// memory.
    /// </param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The rows, or a failure when neither source could be read.</returns>
    public async Task<Result<IReadOnlyList<TunnelSummary>>> ListTunnelsAsync(bool reconcile = false, CancellationToken cancellationToken = default)
    {
        var configured = _store.ListTunnelNames();
        var runtime = await _runtime.ListInterfaceNamesAsync(cancellationToken).ConfigureAwait(false);

        var configuredNames = configured.TryGetValue(out var names) ? names : null;
        IReadOnlyList<string>? runtimeNames = runtime.TryGetValue(out var interfaces) ? interfaces : null;

        if (configuredNames is null && runtimeNames is null)
        {
            return Result<IReadOnlyList<TunnelSummary>>.Failure(configured.Error);
        }

        var known = KnownTunnels();
        var displayNames = DisplayNames();

        var all = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var name in configuredNames ?? [])
        {
            all.Add(name);
        }

        foreach (var name in runtimeNames ?? [])
        {
            all.Add(name);
        }

        if (configuredNames is null)
        {
            // The directory could not be read without elevation; fall back to the
            // application's memory so a configured tunnel stays visible while down.
            foreach (var name in known.Keys)
            {
                all.Add(name);
            }
        }
        else if (reconcile)
        {
            // Only an explicit synchronize rewrites the memory; a passive read (a poll
            // or --diagnostics) must not touch the state document.
            Reconcile(configuredNames);
        }

        var networkManagerConnections = await ListNetworkManagerAsync(cancellationToken).ConfigureAwait(false);
        CacheNetworkManager(networkManagerConnections);
        var networkManagerByInterface = new Dictionary<string, NetworkManagerConnection>(StringComparer.Ordinal);
        foreach (var connection in networkManagerConnections)
        {
            networkManagerByInterface.TryAdd(connection.InterfaceName, connection);
        }

        var storeNote = configuredNames is null ? configured.Error.Message : null;
        var networkManagerByName = new Dictionary<string, NetworkManagerConnection>(StringComparer.Ordinal);
        foreach (var connection in networkManagerConnections)
        {
            networkManagerByName.TryAdd(connection.Name, connection);
        }

        var rows = new List<TunnelSummary>(all.Count + networkManagerConnections.Count);
        var represented = new HashSet<string>(StringComparer.Ordinal);

        foreach (var name in all)
        {
            var connection = networkManagerByName.TryGetValue(name, out var byName)
                ? byName
                : networkManagerByInterface.GetValueOrDefault(name);
            var hasConfiguration = configuredNames?.Contains(name, StringComparer.Ordinal) ?? known.ContainsKey(name);

            if (!hasConfiguration && connection is not null)
            {
                // A connection this application did not create: it is listed and
                // connectable, and only NetworkManager may edit or delete it.
                rows.Add(ForeignRow(connection, runtimeNames, displayNames.GetValueOrDefault(connection.Name)));
                represented.Add(connection.Name);
                continue;
            }

            var peerCount = 0;
            int? listenPort = null;
            string? note = storeNote;

            if (hasConfiguration && configuredNames is not null)
            {
                var read = _store.Read(name);
                if (read.TryGetValue(out var text))
                {
                    var parsed = WireGuardConfigParser.Parse(name, text);
                    if (parsed.TryGetValue(out var configuration))
                    {
                        peerCount = configuration.Peers.Count;
                        listenPort = configuration.Interface.ListenPort;
                    }
                    else
                    {
                        note = parsed.Error.Message;
                    }
                }
                else
                {
                    note = read.Error.Message;
                }
            }
            else if (hasConfiguration && known.TryGetValue(name, out var entry))
            {
                peerCount = entry.PeerCount;
                listenPort = entry.ListenPort;
                note = entry.Verified ? null : "Not verified yet; authenticate to confirm it still exists.";
            }

            // The profile is the application's own even after it has been imported
            // into NetworkManager, so it keeps no "managed" note; only the state
            // follows NetworkManager when the interface is not visible to us.
            var state = runtimeNames is not null
                ? runtimeNames.Contains(name, StringComparer.Ordinal) || connection?.IsActive == true
                    ? TunnelState.Up
                    : TunnelState.Down
                : connection is not null
                    ? connection.IsActive ? TunnelState.Up : TunnelState.Down
                    : TunnelState.Unknown;

            rows.Add(new TunnelSummary(name, state, hasConfiguration, peerCount, listenPort, note, DisplayName: displayNames.GetValueOrDefault(name)));
            represented.Add(name);
            if (connection is not null)
            {
                // The connection id follows the display name, so the foreign-row
                // pass must not list the same connection a second time under it.
                represented.Add(connection.Name);
            }
        }

        foreach (var connection in networkManagerConnections)
        {
            if (represented.Contains(connection.Name))
            {
                continue;
            }

            rows.Add(ForeignRow(connection, runtimeNames, displayNames.GetValueOrDefault(connection.Name)));
            represented.Add(connection.Name);
        }

        rows.Sort(static (left, right) => string.Compare(left.DisplayName ?? left.Name, right.DisplayName ?? right.Name, StringComparison.OrdinalIgnoreCase));
        return Result<IReadOnlyList<TunnelSummary>>.Success(rows);
    }

    // A NetworkManager connection this application did not create: no configuration
    // read here, because the system directory prompts and a plain list must never
    // raise a polkit prompt.
    private static TunnelSummary ForeignRow(NetworkManagerConnection connection, IReadOnlyList<string>? runtimeNames, string? displayName)
    {
        var isUp = connection.IsActive
            || (runtimeNames?.Contains(connection.InterfaceName, StringComparer.Ordinal) ?? false);

        return new TunnelSummary(
            connection.Name,
            isUp ? TunnelState.Up : TunnelState.Down,
            true,
            0,
            null,
            "Managed by NetworkManager",
            TunnelSource.NetworkManager,
            displayName);
    }

    /// <summary>
    /// Lists the NetworkManager WireGuard connections the current session may see:
    /// every one for the system scope, only the owned ones for a named account.
    /// </summary>
    private async Task<Result<IReadOnlyList<NetworkManagerConnection>>> TryListNetworkManagerAsync(CancellationToken cancellationToken)
    {
        if (_networkManager is null)
        {
            return Result<IReadOnlyList<NetworkManagerConnection>>.Success([]);
        }

        var listed = await _networkManager.ListWireGuardConnectionsAsync(cancellationToken).ConfigureAwait(false);
        if (!listed.TryGetValue(out var connections))
        {
            return Result<IReadOnlyList<NetworkManagerConnection>>.Failure(listed.Error);
        }

        var owned = _networkManagerOwner is null
            ? connections
            : connections.Where(connection => connection.IsOwnedBy(_networkManagerOwner)).ToArray();
        return Result<IReadOnlyList<NetworkManagerConnection>>.Success(owned);
    }

    private async Task<IReadOnlyList<NetworkManagerConnection>> ListNetworkManagerAsync(CancellationToken cancellationToken)
    {
        var listed = await TryListNetworkManagerAsync(cancellationToken).ConfigureAwait(false);
        return listed.TryGetValue(out var connections) ? connections : [];
    }

    /// <summary>The connection id a tunnel uses: its display name, or the real name.</summary>
    private string EffectiveId(string name)
    {
        var displayNames = DisplayNames();
        return displayNames.TryGetValue(name, out var displayName) && displayName.Length > 0
            ? displayName
            : name;
    }

    /// <summary>Finds the NetworkManager connection that carries a name, refreshing the cache.</summary>
    private async Task<NetworkManagerConnection?> FindNetworkManagerConnectionAsync(string name, CancellationToken cancellationToken)
    {
        var found = await TryFindNetworkManagerConnectionAsync(name, cancellationToken).ConfigureAwait(false);
        return found.TryGetValue(out var connection) ? connection : null;
    }

    private async Task<Result<NetworkManagerConnection?>> TryFindNetworkManagerConnectionAsync(string name, CancellationToken cancellationToken)
    {
        if (_networkManager is null)
        {
            return Result<NetworkManagerConnection?>.Success(null);
        }

        var listed = await TryListNetworkManagerAsync(cancellationToken).ConfigureAwait(false);
        if (!listed.TryGetValue(out var connections))
        {
            return Result<NetworkManagerConnection?>.Failure(listed.Error);
        }

        CacheNetworkManager(connections);
        var effectiveId = EffectiveId(name);
        // The id follows the display name; the interface is the stable identity.
        return Result<NetworkManagerConnection?>.Success(connections.FirstOrDefault(connection =>
            string.Equals(connection.Name, effectiveId, StringComparison.Ordinal) ||
            string.Equals(connection.InterfaceName, name, StringComparison.Ordinal)));
    }

    /// <summary>Caches the connection names and the interface each one creates.</summary>
    private void CacheNetworkManager(IReadOnlyList<NetworkManagerConnection> connections)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var interfaces = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var connection in connections)
        {
            names.Add(connection.Name);
            interfaces[connection.Name] = connection.InterfaceName;
        }

        _networkManagerNames = names;
        _networkManagerInterfaces = interfaces;
    }

    /// <summary>
    /// Maps a tunnel name to the kernel interface its counters live on: the
    /// interface a NetworkManager connection creates, or the name itself for a
    /// wg-quick file. A connection id is not always an interface name.
    /// </summary>
    /// <param name="tunnelName">The tunnel name.</param>
    /// <returns>The interface name to read the counters from.</returns>
    public string ResolveInterfaceName(string tunnelName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tunnelName);
        return _networkManagerInterfaces.TryGetValue(tunnelName, out var interfaceName) ? interfaceName : tunnelName;
    }

    private Dictionary<string, TunnelStateEntry> KnownTunnels()
    {
        if (_stateStore is null || !_stateStore.Load().TryGetValue(out var state))
        {
            return new Dictionary<string, TunnelStateEntry>(StringComparer.Ordinal);
        }

        return state.Tunnels
            .Where(entry => entry.Scope == _scope)
            .ToDictionary(entry => entry.Name, StringComparer.Ordinal);
    }

    private Dictionary<string, string> DisplayNames()
    {
        if (_stateStore is null || !_stateStore.Load().TryGetValue(out var state))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return state.DisplayNames;
    }

    // When the configured list is readable, it is the truth for which tunnels
    // exist in this scope; the memory is rewritten to match it and the entries are
    // marked verified.
    private void Reconcile(IReadOnlyList<string> configuredNames)
    {
        if (_stateStore is null || !_stateStore.Load().TryGetValue(out var state))
        {
            return;
        }

        var otherScopes = state.Tunnels.Where(entry => entry.Scope != _scope);

        var reconciled = configuredNames
            .Select(name => state.Tunnels.FirstOrDefault(entry => entry.Scope == _scope && string.Equals(entry.Name, name, StringComparison.Ordinal))
                ?? new TunnelStateEntry { Name = name, Scope = _scope, Source = "unknown" })
            .Select(entry => entry with { Verified = true })
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .ToArray();

        var next = otherScopes.Concat(reconciled).ToArray();
        if (state.Tunnels.SequenceEqual(next))
        {
            // The index already matches the truth: a refresh must not rewrite it.
            return;
        }

        _ = _stateStore.Save(state with { Tunnels = next });
    }

    private void Remember(TunnelConfiguration configuration, string source)
    {
        if (_stateStore is null || !_stateStore.Load().TryGetValue(out var state))
        {
            return;
        }

        var existing = state.Tunnels.FirstOrDefault(entry => entry.Scope == _scope && string.Equals(entry.Name, configuration.Name, StringComparison.Ordinal));
        var entry = new TunnelStateEntry
        {
            Name = configuration.Name,
            Scope = _scope,
            Source = existing?.Source ?? source,
            PeerCount = configuration.Peers.Count,
            ListenPort = configuration.Interface.ListenPort,
            UpdatedAt = DateTimeOffset.UtcNow,
            Verified = true,
            AutoReconnect = existing?.AutoReconnect ?? true,
            DnsMode = existing?.DnsMode ?? NetworkManagerDnsMode.Profile,
            DnsSearchDomains = existing?.DnsSearchDomains ?? [],
        };

        var tunnels = state.Tunnels
            .Where(other => !(other.Scope == _scope && string.Equals(other.Name, entry.Name, StringComparison.Ordinal)))
            .Append(entry)
            .OrderBy(other => other.Name, StringComparer.Ordinal)
            .ToArray();

        _ = _stateStore.Save(state with { Tunnels = tunnels });
    }

    private void Forget(string name)
    {
        if (_stateStore is null || !_stateStore.Load().TryGetValue(out var state))
        {
            return;
        }

        var tunnels = state.Tunnels
            .Where(entry => !(entry.Scope == _scope && string.Equals(entry.Name, name, StringComparison.Ordinal)))
            .ToArray();

        var displayNames = new Dictionary<string, string>(state.DisplayNames, StringComparer.Ordinal);
        displayNames.Remove(name);

        _ = _stateStore.Save(state with { Tunnels = tunnels, DisplayNames = displayNames });
    }

    /// <summary>Reads and parses the stored configuration of a tunnel.</summary>
    /// <param name="name">The tunnel name.</param>
    /// <returns>The configuration, or a failure.</returns>
    public Result<TunnelConfiguration> LoadConfiguration(string name)
    {
        // A file the application owns is the profile's truth, even after it has been
        // imported into NetworkManager; a connection with no file is foreign.
        var exists = _store.Exists(name);
        if (exists.TryGetValue(out var hasFile) && hasFile)
        {
            var text = _store.Read(name);
            return text.TryGetValue(out var content)
                ? WireGuardConfigParser.Parse(name, content)
                : Result<TunnelConfiguration>.Failure(text.Error);
        }

        return _networkManagerNames.Contains(name)
            ? Result<TunnelConfiguration>.Failure(WireGuardErrors.NetworkManagerManaged(name))
            : Result<TunnelConfiguration>.Failure(WireGuardErrors.ConfigNotFound(name));
    }

    /// <summary>Renders a configuration as wg-quick text.</summary>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The text.</returns>
    public static string Render(TunnelConfiguration configuration) => WireGuardConfigWriter.Write(configuration);

    /// <summary>
    /// Sets the operator's display name for a tunnel. It changes only what the
    /// application shows: the configuration file, the interface and the
    /// NetworkManager connection id follows the display name. An empty name clears
    /// the alias and restores the real connection id.
    /// </summary>
    /// <param name="name">The real tunnel name.</param>
    /// <param name="displayName">The display name, or empty to show the real name.</param>
    /// <returns>The name now shown (the real name when cleared), or a failure.</returns>
    public async Task<Result<string>> SetDisplayName(
        string name,
        string? displayName,
        CancellationToken cancellationToken = default)
    {
        if (_readOnly)
        {
            return Result<string>.Failure(WireGuardErrors.ReadOnly(nameof(SetDisplayName)));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<string>.Failure(WireGuardErrors.Validation(
                WireGuardErrorCodes.TunnelNameEmpty,
                "A tunnel name is required to rename it."));
        }

        var trimmed = displayName?.Trim() ?? string.Empty;
        if (trimmed.Length > 64)
        {
            return Result<string>.Failure(WireGuardErrors.Validation(
                WireGuardErrorCodes.DisplayNameInvalid,
                "A display name is at most 64 characters."));
        }

        // The display name becomes the NetworkManager connection id: it is one
        // command argument, so an option-shaped or control-bearing value is refused.
        if (trimmed.StartsWith('-') || trimmed.Any(char.IsControl))
        {
            return Result<string>.Failure(WireGuardErrors.Validation(
                WireGuardErrorCodes.DisplayNameInvalid,
                "A display name must not start with '-' or contain control characters."));
        }

        if (_stateStore is null || !_stateStore.Load().TryGetValue(out var state))
        {
            return Result<string>.Failure(WireGuardErrors.StateUnavailable());
        }

        var effectiveId = trimmed.Length == 0 || string.Equals(trimmed, name, StringComparison.Ordinal)
            ? name
            : trimmed;
        var names = new Dictionary<string, string>(state.DisplayNames, StringComparer.Ordinal);

        var configured = _store.ListTunnelNames();
        var others = (configured.IsSuccess ? configured.Value : [])
            .Concat(state.Tunnels.Where(entry => entry.Scope == _scope).Select(entry => entry.Name))
            .Where(other => !string.Equals(other, name, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal);
        foreach (var other in others)
        {
            var otherId = names.TryGetValue(other, out var alias) && alias.Length > 0 ? alias : other;
            if (string.Equals(otherId, effectiveId, StringComparison.Ordinal))
            {
                return Result<string>.Failure(WireGuardErrors.Validation(
                    WireGuardErrorCodes.DisplayNameInvalid,
                    $"The display name '{effectiveId}' is already used by '{other}'."));
            }
        }

        NetworkManagerConnection? owned = null;
        var needsRename = false;
        if (_networkManager is not null)
        {
            var found = await TryFindNetworkManagerConnectionAsync(name, cancellationToken).ConfigureAwait(false);
            if (!found.IsSuccess)
            {
                return Result<string>.Failure(found.Error);
            }

            owned = found.Value;
            if (owned is not null)
            {
                if (_networkManagerOwner is { } owner && !owned.IsOwnedBy(owner))
                {
                    return Result<string>.Failure(WireGuardErrors.NetworkManagerManaged(name));
                }

                var listed = await _networkManager.ListWireGuardConnectionsAsync(cancellationToken).ConfigureAwait(false);
                if (listed.TryGetValue(out var connections) &&
                    connections.Any(other =>
                        string.Equals(other.Name, effectiveId, StringComparison.Ordinal) &&
                        !string.Equals(other.InterfaceName, name, StringComparison.Ordinal)))
                {
                    return Result<string>.Failure(WireGuardErrors.Validation(
                        WireGuardErrorCodes.DisplayNameInvalid,
                        $"A NetworkManager connection is already named '{effectiveId}'."));
                }

                needsRename = !string.Equals(owned.Name, effectiveId, StringComparison.Ordinal);
            }
        }

        if (effectiveId == name)
        {
            names.Remove(name);
        }
        else
        {
            names[name] = effectiveId;
        }

        // Store the alias before touching NetworkManager: if the state cannot be
        // written, the rename never happened, so the two cannot diverge silently.
        var saved = _stateStore.Save(state with { DisplayNames = names });
        if (!saved.IsSuccess)
        {
            return Result<string>.Failure(saved.Error);
        }

        if (owned is not null && needsRename)
        {
            var renamed = await _networkManager!
                .SetConnectionIdAsync(owned.Name, effectiveId, cancellationToken)
                .ConfigureAwait(false);
            if (!renamed.IsSuccess)
            {
                // Best effort: put the old alias back so the app and NetworkManager
                // keep the same name after a refused rename.
                _ = _stateStore.Save(state with { DisplayNames = state.DisplayNames });
                return Result<string>.Failure(renamed.Error);
            }
        }

        return Result<string>.Success(effectiveId);
    }

    /// <summary>Validates a configuration without touching the file system.</summary>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The configuration, or the first failure.</returns>
    public static Result<TunnelConfiguration> Validate(TunnelConfiguration configuration) => TunnelValidator.Validate(configuration);

    /// <summary>Creates a new tunnel configuration file.</summary>
    /// <param name="configuration">The configuration; it is validated first.</param>
    /// <returns>The written path, or a failure.</returns>
    public Result<string> Create(TunnelConfiguration configuration)
    {
        if (_readOnly)
        {
            return Result<string>.Failure(WireGuardErrors.ReadOnly(nameof(Create)));
        }

        var validated = TunnelValidator.Validate(configuration);
        if (!validated.TryGetValue(out var value))
        {
            return Result<string>.Failure(validated.Error);
        }

        var exists = _store.Exists(value.Name);
        if (exists.TryGetValue(out var alreadyExists) && alreadyExists)
        {
            return Result<string>.Failure(WireGuardErrors.ConfigExists(value.Name));
        }

        var written = _store.Write(value.Name, WireGuardConfigWriter.Write(value));
        if (written.IsSuccess)
        {
            Remember(value, "created");
        }

        return written;
    }

    /// <summary>Replaces an existing tunnel configuration file.</summary>
    /// <param name="configuration">The configuration; it is validated first.</param>
    /// <returns>The written path, or a failure.</returns>
    public Result<string> Update(TunnelConfiguration configuration)
    {
        if (_readOnly)
        {
            return Result<string>.Failure(WireGuardErrors.ReadOnly(nameof(Update)));
        }

        var validated = TunnelValidator.Validate(configuration);
        if (!validated.TryGetValue(out var value))
        {
            return Result<string>.Failure(validated.Error);
        }

        var exists = _store.Exists(value.Name);
        if (!exists.TryGetValue(out var alreadyExists))
        {
            return Result<string>.Failure(exists.Error);
        }

        if (!alreadyExists)
        {
            return Result<string>.Failure(WireGuardErrors.ConfigNotFound(value.Name));
        }

        var written = _store.Write(value.Name, WireGuardConfigWriter.Write(value));
        if (written.IsSuccess)
        {
            Remember(value, "created");
        }

        return written;
    }

    /// <summary>Deletes a tunnel configuration file.</summary>
    /// <param name="name">The tunnel name.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The deleted path, or a failure.</returns>
    public async Task<Result<string>> DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        if (_readOnly)
        {
            return Result<string>.Failure(WireGuardErrors.ReadOnly(nameof(DeleteAsync)));
        }

        if (!TunnelNameRules.IsValid(name))
        {
            return Result<string>.Failure(WireGuardErrors.Validation(
                WireGuardErrorCodes.TunnelNameInvalid,
                $"'{name}' is not a usable tunnel name."));
        }

        var exists = _store.Exists(name);
        if (!exists.TryGetValue(out var hasFile) || !hasFile)
        {
            var connection = await FindNetworkManagerConnectionAsync(name, cancellationToken).ConfigureAwait(false);
            return Result<string>.Failure(connection is not null
                ? WireGuardErrors.NetworkManagerManaged(name)
                : WireGuardErrors.ConfigNotFound(name));
        }

        var runtime = await _runtime.ListInterfaceNamesAsync(cancellationToken).ConfigureAwait(false);
        if (runtime.TryGetValue(out var names) && names.Contains(name, StringComparer.Ordinal))
        {
            return Result<string>.Failure(WireGuardErrors.Validation(
                WireGuardErrorCodes.TunnelAlreadyUp,
                $"Tunnel '{name}' is up; stop it before deleting its configuration."));
        }

        var deleted = _store.Delete(name);
        if (deleted.IsSuccess)
        {
            // The profile is imported into NetworkManager; deleting the file must not
            // leave the operator's own connection behind. A connection this account
            // does not own is never deleted here.
            if (_networkManager is not null)
            {
                var owned = await FindNetworkManagerConnectionAsync(name, cancellationToken).ConfigureAwait(false);
                if (owned is not null && string.Equals(owned.Name, EffectiveId(name), StringComparison.Ordinal))
                {
                    _ = await _networkManager.DeleteAsync(owned.Name, cancellationToken).ConfigureAwait(false);
                }
            }

            Forget(name);
        }

        return deleted;
    }

    /// <summary>Brings a tunnel up through NetworkManager.</summary>
    /// <param name="name">The tunnel name.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The tunnel name, or a failure.</returns>
    public async Task<Result<string>> StartAsync(string name, CancellationToken cancellationToken = default)
    {
        if (_readOnly)
        {
            return Result<string>.Failure(WireGuardErrors.ReadOnly(nameof(StartAsync)));
        }

        if (_networkManager is null)
        {
            return Result<string>.Failure(WireGuardErrors.NetworkManagerUnavailable());
        }

        // Only one tunnel may be connected at a time: a second full tunnel fights
        // the first for the default route and the DNS. Refuse, and name the tunnel
        // to disconnect first.
        var listed = await ListTunnelsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (listed.TryGetValue(out var rows))
        {
            var active = rows.FirstOrDefault(row => row.State == TunnelState.Up
                && !string.Equals(row.Name, name, StringComparison.Ordinal));
            if (active is not null)
            {
                return Result<string>.Failure(WireGuardErrors.TunnelAlreadyConnected(active.DisplayName ?? active.Name, name));
            }
        }

        // NetworkManager is the machine's engine: the profile file is imported as
        // the operator's own connection and activated, without root.
        var ensured = await EnsureNetworkManagerAsync(name, cancellationToken).ConfigureAwait(false);
        if (!ensured.IsSuccess)
        {
            return Result<string>.Failure(ensured.Error);
        }

        var connection = await FindNetworkManagerConnectionAsync(name, cancellationToken).ConfigureAwait(false);
        return await _networkManager
            .ActivateAsync(connection?.Name ?? name, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Takes a tunnel down through NetworkManager.</summary>
    /// <param name="name">The tunnel name.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The tunnel name, or a failure.</returns>
    public async Task<Result<string>> StopAsync(string name, CancellationToken cancellationToken = default)
    {
        if (_readOnly)
        {
            return Result<string>.Failure(WireGuardErrors.ReadOnly(nameof(StopAsync)));
        }

        if (_networkManager is null)
        {
            return Result<string>.Failure(WireGuardErrors.NetworkManagerUnavailable());
        }

        var found = await TryFindNetworkManagerConnectionAsync(name, cancellationToken).ConfigureAwait(false);
        if (!found.IsSuccess)
        {
            // A failed listing must not be reported as "already disconnected".
            return Result<string>.Failure(found.Error);
        }

        var connection = found.Value;
        var connectionId = connection?.Name ?? name;

        // A disconnect on a tunnel that is already down is a no-op, not an
        // nmcli error: the operator asked for a state, not for a transition.
        var down = connection is null || !connection.IsActive
            ? Result<string>.Success(connectionId)
            : await _networkManager.DeactivateAsync(connectionId, cancellationToken).ConfigureAwait(false);

        if (down.IsSuccess &&
            connection is not null &&
            _store.Exists(name).TryGetValue(out var hasFile) &&
            hasFile)
        {
            // A manual disconnect is authoritative: NetworkManager must not bring
            // the profile back until the operator connects again.
            _ = await _networkManager
                .SetAutoConnectAsync(connectionId, autoConnect: false, priority: 0, cancellationToken)
                .ConfigureAwait(false);
        }

        return down;
    }

    /// <summary>Gets the stored policy for a tunnel, or the default when none is saved.</summary>
    /// <param name="name">The tunnel name.</param>
    /// <returns>The policy.</returns>
    public TunnelPolicy GetPolicy(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var entry = KnownTunnels().GetValueOrDefault(name);
        return entry is null
            ? TunnelPolicy.Default
            : new TunnelPolicy(entry.AutoReconnect, entry.DnsMode, entry.DnsSearchDomains);
    }

    /// <summary>
    /// Stores the auto-reconnect and DNS policy and applies it to an owned
    /// NetworkManager connection when one exists.
    /// </summary>
    /// <param name="name">The tunnel name.</param>
    /// <param name="policy">The policy to store.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The stored policy, or a failure.</returns>
    public async Task<Result<TunnelPolicy>> SetPolicyAsync(
        string name,
        TunnelPolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(policy);

        if (_readOnly)
        {
            return Result<TunnelPolicy>.Failure(WireGuardErrors.ReadOnly(nameof(SetPolicyAsync)));
        }

        if (!TunnelNameRules.IsValid(name))
        {
            return Result<TunnelPolicy>.Failure(WireGuardErrors.Validation(
                WireGuardErrorCodes.TunnelNameInvalid,
                $"'{name}' is not a usable tunnel name."));
        }

        if (!Enum.IsDefined(policy.DnsMode))
        {
            return Result<TunnelPolicy>.Failure(WireGuardErrors.Validation(
                WireGuardErrorCodes.DnsInvalid,
                $"'{policy.DnsMode}' is not a supported DNS mode."));
        }

        foreach (var domain in policy.SearchDomains)
        {
            if (!NetworkText.IsValidDnsEntry(domain))
            {
                return Result<TunnelPolicy>.Failure(WireGuardErrors.Validation(
                    WireGuardErrorCodes.DnsInvalid,
                    $"Search domain '{domain}' is not a usable host name or routing domain."));
            }
        }

        var hasFile = _store.Exists(name);
        var connection = await FindNetworkManagerConnectionAsync(name, cancellationToken).ConfigureAwait(false);
        if ((!hasFile.IsSuccess || !hasFile.Value) && connection is null)
        {
            return Result<TunnelPolicy>.Failure(WireGuardErrors.ConfigNotFound(name));
        }

        if (_stateStore is null)
        {
            return Result<TunnelPolicy>.Failure(WireGuardErrors.Configuration(
                WireGuardErrorCodes.StateUnavailable,
                "The application has no state store, so the policy cannot be remembered."));
        }

        var loaded = _stateStore.Load();
        if (!loaded.TryGetValue(out var state))
        {
            return Result<TunnelPolicy>.Failure(loaded.Error);
        }

        var existing = state.Tunnels.FirstOrDefault(entry =>
            entry.Scope == _scope &&
            string.Equals(entry.Name, name, StringComparison.Ordinal));

        var updated = (existing ?? new TunnelStateEntry
        {
            Name = name,
            Scope = _scope,
            Source = "unknown",
        }) with
        {
            AutoReconnect = policy.AutoReconnect,
            DnsMode = policy.DnsMode,
            DnsSearchDomains = policy.SearchDomains.ToArray(),
            UpdatedAt = DateTimeOffset.UtcNow,
            Verified = false,
        };

        var tunnels = state.Tunnels
            .Where(other => !(other.Scope == _scope && string.Equals(other.Name, name, StringComparison.Ordinal)))
            .Append(updated)
            .OrderBy(other => other.Name, StringComparer.Ordinal)
            .ToArray();

        var saved = _stateStore.Save(state with { Tunnels = tunnels });
        if (!saved.IsSuccess)
        {
            return Result<TunnelPolicy>.Failure(saved.Error);
        }

        var read = _store.Read(name);
        if (connection is not null && read.TryGetValue(out var text))
        {
            var parsed = WireGuardConfigParser.Parse(name, text);
            if (parsed.TryGetValue(out var configuration))
            {
                var applied = await ApplyPolicyAsync(name, connection.Name, configuration, reapplyWhenActive: true, cancellationToken).ConfigureAwait(false);
                if (!applied.IsSuccess)
                {
                    return Result<TunnelPolicy>.Failure(applied.Error);
                }
            }
        }

        return Result<TunnelPolicy>.Success(policy);
    }

    private async Task<Result<string>> ApplyPolicyAsync(
        string name,
        string connectionId,
        TunnelConfiguration configuration,
        bool reapplyWhenActive,
        CancellationToken cancellationToken)
    {
        if (_networkManager is null)
        {
            return Result<string>.Success(name);
        }

        var policy = GetPolicy(name);
        var auto = await _networkManager
            .SetAutoConnectAsync(connectionId, policy.AutoReconnect, priority: 0, cancellationToken)
            .ConfigureAwait(false);
        if (!auto.IsSuccess)
        {
            return auto;
        }

        var includeIpv6 = configuration.Interface.Addresses
            .Select(address => address.Split('/', 2)[0])
            .Any(address => IPAddress.TryParse(address, out var parsed) &&
                parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6);

        var applied = await _networkManager
            .ApplyDnsAsync(
                connectionId,
                configuration.Interface.Dns,
                policy.SearchDomains,
                policy.DnsMode,
                includeIpv6,
                cancellationToken)
            .ConfigureAwait(false);
        if (!applied.IsSuccess)
        {
            return applied;
        }

        if (!reapplyWhenActive)
        {
            // The connect path applies the policy to the profile and then activates
            // it, so the settings take effect at activation. Reapplying here races
            // the autoconnect that may already have brought the new profile up, and
            // nmcli refuses the reapply with "Can't reapply changes to
            // 'connection.permissions' setting" while the tunnel is up.
            return applied;
        }

        // An explicit policy change on a live connection must hold at once:
        // `nmcli connection modify` updated the stored profile only, so reapply
        // the IP configuration of the active device.
        var connection = await FindNetworkManagerConnectionAsync(connectionId, cancellationToken).ConfigureAwait(false);
        return connection is { IsActive: true }
            ? await _networkManager.ReapplyAsync(connection.InterfaceName, cancellationToken).ConfigureAwait(false)
            : applied;
    }

    /// <summary>
    /// Reads where a tunnel's link resolves names. It is a read-only probe: it
    /// reports the routing domains the link carries, so an uncontained link is
    /// visible instead of assumed safe.
    /// </summary>
    /// <param name="name">The tunnel name.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The resolver state, or a failure when it cannot be read.</returns>
    public Task<Result<DnsState>> DescribeDnsAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return _dnsStateReader is null
            ? Task.FromResult(Result<DnsState>.Failure(WireGuardErrors.DnsUnavailable()))
            : _dnsStateReader.ReadAsync(ResolveInterfaceName(name), cancellationToken);
    }

    /// <summary>
    /// Describes a profile's IPv6 against the host. It is read-only: it reports a
    /// partial IPv6 and a host without a global IPv6 address, so the connect path can
    /// avoid a half-configured tunnel.
    /// </summary>
    /// <param name="name">The tunnel name.</param>
    /// <returns>The description, or a failure.</returns>
    public Result<Ipv6Description> DescribeIpv6(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var read = _store.Read(name);
        if (!read.TryGetValue(out var text))
        {
            return Result<Ipv6Description>.Failure(read.Error);
        }

        var parsed = WireGuardConfigParser.Parse(name, text);
        if (!parsed.TryGetValue(out var configuration))
        {
            return Result<Ipv6Description>.Failure(parsed.Error);
        }

        var hostHasIpv6 = _fileSystem is not null && Ipv6Host.HasGlobalAddress(_fileSystem);
        return Result<Ipv6Description>.Success(new Ipv6Description(Ipv6Profile.Classify(configuration), hostHasIpv6));
    }

    // Renders the stored configuration to a temporary file and hands it to
    // NetworkManager, which imports it as the operator's own connection.
    private async Task<Result<string>> EnsureNetworkManagerAsync(string name, CancellationToken cancellationToken)
    {
        if (_networkManagerOwner is null)
        {
            return Result<string>.Success(name);
        }

        var exists = _store.Exists(name);
        if (!exists.TryGetValue(out var hasFile) || !hasFile)
        {
            // A connection this application did not create: NetworkManager already
            // holds it, so there is nothing to import, only to activate.
            return Result<string>.Success(name);
        }

        var read = _store.Read(name);
        if (!read.TryGetValue(out var text))
        {
            return Result<string>.Failure(read.Error);
        }

        // A stored profile is untrusted input: it may have been edited outside the
        // product. Refuse before NetworkManager sees it, rather than passing a raw
        // text that could carry hooks or an unsupported key.
        var parsed = WireGuardConfigParser.Parse(name, text);
        if (!parsed.TryGetValue(out var configuration))
        {
            return Result<string>.Failure(WireGuardErrors.Configuration(
                WireGuardErrorCodes.ConfigParseError,
                parsed.Error.Message));
        }

        var validated = TunnelValidator.Validate(configuration);
        if (!validated.TryGetValue(out var validConfiguration))
        {
            return Result<string>.Failure(validated.Error);
        }

        // NetworkManager does not execute wg-quick hooks. The stored profile keeps
        // them, but the copy handed to NetworkManager never carries a command the
        // product cannot honour.
        var importConfiguration = validConfiguration with
        {
            Interface = validConfiguration.Interface with
            {
                PreUp = [],
                PostUp = [],
                PreDown = [],
                PostDown = [],
            },
        };

        // `nmcli connection import` requires the file stem to be a valid interface
        // name, so the profile is written as <name>.conf inside a unique directory.
        // The import directory is supplied by the application (owner-only); the
        // per-import child is forced owner-only as well.
        Directory.CreateDirectory(_importDirectory);
        var directory = Path.Combine(_importDirectory, string.Concat("import-", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        File.SetUnixFileMode(
            directory,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var temp = Path.Combine(directory, string.Concat(name, ".conf"));
        var written = _store.WriteFile(temp, PrepareImportText(importConfiguration));
        if (!written.TryGetValue(out _))
        {
            return Result<string>.Failure(written.Error);
        }

        try
        {
            var connectionId = EffectiveId(name);
            var ensured = await _networkManager!.EnsureAsync(connectionId, name, temp, _networkManagerOwner, cancellationToken).ConfigureAwait(false);
            return ensured.IsSuccess
                ? await ApplyPolicyAsync(name, connectionId, validConfiguration, reapplyWhenActive: false, cancellationToken).ConfigureAwait(false)
                : Result<string>.Failure(ensured.Error);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A leftover temporary directory is harmless.
            }
        }
    }

    // NetworkManager imports the rendered profile. A partial IPv6, or a complete one
    // on a host without a global IPv6 address, would be half-configured; import a
    // consistent IPv4-only copy and leave the stored profile untouched.
    private string PrepareImportText(TunnelConfiguration configuration)
    {
        var completeness = Ipv6Profile.Classify(configuration);
        if (completeness == Ipv6Completeness.None)
        {
            return WireGuardConfigWriter.Write(configuration);
        }

        if (completeness == Ipv6Completeness.Complete && _fileSystem is not null && Ipv6Host.HasGlobalAddress(_fileSystem))
        {
            return WireGuardConfigWriter.Write(configuration);
        }

        return WireGuardConfigWriter.Write(Ipv6Profile.StripIpv6(configuration));
    }

    /// <summary>
    /// Reads a configuration file from another location and stores it as a
    /// tunnel after parsing and validating it.
    /// </summary>
    /// <param name="name">The tunnel name to store it under.</param>
    /// <param name="sourcePath">The file to read.</param>
    /// <returns>The stored path, or a failure.</returns>
    public Result<string> Import(string name, string sourcePath)
    {
        if (_readOnly)
        {
            return Result<string>.Failure(WireGuardErrors.ReadOnly(nameof(Import)));
        }

        if (!TunnelNameRules.IsValid(name))
        {
            return Result<string>.Failure(WireGuardErrors.Validation(
                WireGuardErrorCodes.TunnelNameInvalid,
                $"'{name}' is not a usable tunnel name."));
        }

        var read = _store.ReadFile(sourcePath);
        if (!read.TryGetValue(out var text))
        {
            return Result<string>.Failure(read.Error);
        }

        var parsed = WireGuardConfigParser.Parse(name, text);
        if (!parsed.TryGetValue(out var configuration))
        {
            return Result<string>.Failure(WireGuardErrors.Configuration(
                WireGuardErrorCodes.ImportInvalid,
                parsed.Error.Message));
        }

        var validated = TunnelValidator.Validate(configuration);
        if (!validated.TryGetValue(out var value))
        {
            return Result<string>.Failure(WireGuardErrors.Configuration(
                WireGuardErrorCodes.ImportInvalid,
                validated.Error.Message));
        }

        if (KnownTunnels().ContainsKey(name))
        {
            return Result<string>.Failure(WireGuardErrors.Configuration(
                WireGuardErrorCodes.TunnelNameConflict,
                $"A configuration for '{name}' already exists; choose another name or edit it instead."));
        }

        var exists = _store.Exists(name);
        if (exists.TryGetValue(out var alreadyExists) && alreadyExists)
        {
            return Result<string>.Failure(WireGuardErrors.Configuration(
                WireGuardErrorCodes.TunnelNameConflict,
                $"A configuration for '{name}' already exists; choose another name or edit it instead."));
        }

        var written = _store.Write(value.Name, WireGuardConfigWriter.Write(value));
        if (written.IsSuccess)
        {
            Remember(value, "imported");
        }

        return written;
    }

    /// <summary>
    /// Writes a stored configuration to another location. The exported text is
    /// the configuration itself, which necessarily contains the private key;
    /// it is written to the file and never rendered.
    /// </summary>
    /// <param name="name">The tunnel name.</param>
    /// <param name="destinationPath">The file to write.</param>
    /// <returns>The written path, or a failure.</returns>
    public Result<string> Export(string name, string destinationPath)
    {
        var read = _store.Read(name);
        if (!read.TryGetValue(out var text))
        {
            return Result<string>.Failure(read.Error);
        }

        var existing = _store.FileExists(destinationPath);
        if (existing.TryGetValue(out var exists) && exists)
        {
            return Result<string>.Failure(WireGuardErrors.Configuration(
                WireGuardErrorCodes.ExportFailed,
                $"'{destinationPath}' already exists; choose a different file."));
        }

        var write = _store.WriteFile(destinationPath, text);
        return write.IsSuccess
            ? write
            : Result<string>.Failure(WireGuardErrors.Configuration(WireGuardErrorCodes.ExportFailed, write.Error.Message));
    }

    /// <summary>Generates a private key with the WireGuard tool.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The key, or a failure.</returns>
    public Task<Result<SecretKey>> GeneratePrivateKeyAsync(CancellationToken cancellationToken = default) =>
        _runtime.GeneratePrivateKeyAsync(cancellationToken);

    /// <summary>Generates a preshared key with the WireGuard tool.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The key, or a failure.</returns>
    public Task<Result<SecretKey>> GeneratePresharedKeyAsync(CancellationToken cancellationToken = default) =>
        _runtime.GeneratePresharedKeyAsync(cancellationToken);

    /// <summary>Derives a public key from a private key.</summary>
    /// <param name="privateKey">The private key.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The public key, or a failure.</returns>
    public Task<Result<string>> DerivePublicKeyAsync(SecretKey privateKey, CancellationToken cancellationToken = default) =>
        _runtime.DerivePublicKeyAsync(privateKey, cancellationToken);
}
