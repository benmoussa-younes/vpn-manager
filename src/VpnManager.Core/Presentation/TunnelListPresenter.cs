using System.Globalization;
using Core.Mvp.Presenters;
using Core.Primitives;
using VpnManager.Core.Configuration;
using VpnManager.Core.Model;
using VpnManager.Core.NetworkManager;
using VpnManager.Core.Notifications;
using VpnManager.Core.Runtime;
using VpnManager.Core.Services;
using VpnManager.Core.State;

namespace VpnManager.Core.Presentation;

/// <summary>
/// Owns every decision in the main window: which tunnel is selected, what the
/// editor holds, and what each command does. It talks to the view only through
/// <see cref="ITunnelListView"/> and to the machine only through
/// <see cref="TunnelService"/>.
/// </summary>
/// <remarks>
/// <para>
/// The private key of the tunnel being edited is held in this object's private
/// state and is never placed in an <see cref="EditorSnapshot"/>, so the view
/// cannot render it. When the operator generates a key pair, only the derived
/// public key is pushed; the private half waits here until save.
/// </para>
/// <para>
/// Event handlers start asynchronous work through <see cref="RunGuarded"/>, so
/// a failure becomes a status line instead of an unobserved task or a crash.
/// </para>
/// </remarks>
public sealed class TunnelListPresenter : PresenterBase<ITunnelListView>
{
    private static readonly TransferView ZeroTransfer = new("0 B/s", "0 B", "0 B/s", "0 B");

    private static readonly TimeSpan ReconnectBackoff = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _timeProvider;
    private readonly IPhysicalLinkReader? _physicalLink;
    private readonly IRoutingPrerequisiteReader? _routingPrerequisite;
    private TunnelService _service;
    private IOperationJournal? _journal;
    private INotifier? _notifier;
    private INetworkCounterReader? _counterReader;
    private EditorSession? _editor;
    private string? _selectedName;
    private IReadOnlyList<TunnelSummary>? _lastSummaries;
    private int _operationsInFlight;
    private string? _linkKey;
    private bool _linkKnown;
    private DateTimeOffset? _lastReconnectAt;
    private readonly Dictionary<string, TransferSample> _transferSamples = new(StringComparer.Ordinal);
    private IConnectivityReader? _connectivity;
    private NetworkConnectivity _lastConnectivity = NetworkConnectivity.Unknown;
    private bool _connectivityKnown;

    /// <summary>Initializes the presenter and subscribes it to the view.</summary>
    /// <param name="view">The passive view.</param>
    /// <param name="service">The application operations.</param>
    /// <param name="timeProvider">The clock used to compute transfer rates.</param>
    /// <param name="counterReader">The kernel counter reader, or <see langword="null"/> to leave the transfer at rest.</param>
    /// <param name="physicalLink">The physical default-link reader, or <see langword="null"/> to disable reconnect.</param>
    /// <param name="routingPrerequisite">The full-tunnel prerequisite reader, or <see langword="null"/> to skip the warning.</param>
    /// <param name="notifier">The desktop notifier, or <see langword="null"/> when there is none.</param>
    /// <param name="journal">The operation journal, or <see langword="null"/> when there is none.</param>
    public TunnelListPresenter(
        ITunnelListView view,
        TunnelService service,
        TimeProvider? timeProvider = null,
        INetworkCounterReader? counterReader = null,
        IPhysicalLinkReader? physicalLink = null,
        IRoutingPrerequisiteReader? routingPrerequisite = null,
        INotifier? notifier = null,
        IOperationJournal? journal = null,
        IConnectivityReader? connectivity = null)
        : base(view)
    {
        ArgumentNullException.ThrowIfNull(service);

        _service = service;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _counterReader = counterReader;
        _physicalLink = physicalLink;
        _routingPrerequisite = routingPrerequisite;
        _notifier = notifier;
        _connectivity = connectivity;
        _journal = journal;

        view.RefreshRequested += () => RunGuarded(RefreshAsync);
        view.TunnelSelected += name => RunGuarded(() => SelectAsync(name));
        view.CreateRequested += BeginCreate;
        view.DeleteRequested += name => RunGuarded(() => DeleteAsync(name));
        view.StartRequested += name => RunGuarded(() => StartAsync(name));
        view.StopRequested += name => RunGuarded(() => StopAsync(name));
        view.ImportRequested += path => RunGuarded(() => ImportAsync(path));
        view.ProfileTextPasted += text => RunGuarded(() => BeginCreateFromTextAsync(text));
        view.ExportRequested += path => RunGuarded(() => ExportAsync(path));
        view.SaveRequested += input => RunGuarded(() => SaveAsync(input));
        view.EditorCancelRequested += CancelEditor;
        view.EditorPeerAddRequested += AddPeer;
        view.EditorPeerRemoveRequested += RemovePeer;
        view.EditorKeyPairRequested += () => RunGuarded(GenerateKeyPairAsync);
        view.RenameSubmitted += input => RunGuarded(() => RenameAsync(input));
        view.PolicySubmitted += input => RunGuarded(() => ApplyPolicyAsync(input));
    }

    /// <summary>Gets the selected tunnel name, or <see langword="null"/>.</summary>
    public string? SelectedTunnel => _selectedName;

    /// <summary>Gets the most recent operation the presenter started from a view event, or
    /// <see langword="null"/> when none has started. The event handlers return void,
    /// so this is how a host — or a test — awaits the work they began.</summary>
    public Task? PendingOperation { get; private set; }

    private void Journal(string message) => _ = _journal?.Record(message);

    /// <inheritdoc />
    protected override ValueTask OnInitializeAsync(CancellationToken cancellationToken) =>
        new(RefreshCoreAsync(reconcile: false));

    /// <summary>Reads the tunnel list and the live state of the selection.</summary>
    /// <returns>A task that completes when the view has been updated.</returns>
    public async Task RefreshAsync() => await RefreshCoreAsync(reconcile: true).ConfigureAwait(true);

    private async Task RefreshCoreAsync(bool reconcile)
    {
        View.SetBusy(true);
        View.SetError(null);

        try
        {
            var tunnels = await Task.Run(() => _service.ListTunnelsAsync(reconcile)).ConfigureAwait(true);
            if (!tunnels.TryGetValue(out var summaries))
            {
                // Keep whatever is already on screen: a failed refresh must
                // not wipe the list the operator is looking at.
                View.SetError(tunnels.Error.Message);
                return;
            }

            ApplySummaries(summaries);
        }
        finally
        {
            View.SetBusy(false);
        }
    }

    /// <summary>
    /// Re-reads the tunnel list and updates the view only when a row changed,
    /// without the busy overlay and without raising an error. It is the quiet poll
    /// that makes a tunnel toggled outside the application — in GNOME Settings, for
    /// example — appear here.
    /// </summary>
    /// <returns><see langword="true"/> when the view was updated.</returns>
    public async Task<bool> PollStateAsync()
    {
        // Never interfere with an operation the operator started: it refreshes the
        // list itself once it completes.
        if (_operationsInFlight > 0)
        {
            return false;
        }

        var tunnels = await Task.Run(() => _service.ListTunnelsAsync()).ConfigureAwait(true);
        if (!tunnels.TryGetValue(out var summaries))
        {
            // A quiet poll stays quiet: keep the screen and the last error.
            return false;
        }

        // Connectivity can change while the tunnel rows do not (a full tunnel keeps
        // its interface when the link is gone), so it is checked on every poll.
        await CheckConnectivityAsync(CancellationToken.None).ConfigureAwait(true);

        if (_lastSummaries is not null && _lastSummaries.SequenceEqual(summaries))
        {
            return false;
        }

        ApplySummaries(summaries);
        return true;
    }

    private void ApplySummaries(IReadOnlyList<TunnelSummary> summaries)
    {
        var previous = _lastSummaries;
        _lastSummaries = summaries;

        // A state change seen outside an operation is the operator's link moving:
        // the window is not the place to announce it.
        if (previous is not null && _operationsInFlight == 0)
        {
            NotifyTransitions(previous, summaries);
        }

        if (_selectedName is not null && !summaries.Any(summary => string.Equals(summary.Name, _selectedName, StringComparison.Ordinal)))
        {
            _selectedName = null;
        }

        View.ShowTunnels(summaries.Select(ToListItem).ToArray(), _selectedName);
        View.SetStatus(summaries.Count == 1 ? "1 tunnel." : $"{summaries.Count} tunnels.");
    }

    /// <summary>
    /// Re-establishes an owned tunnel after the physical link moved. It reads the
    /// world-readable default route, acts only on a real change (never on idle
    /// traffic), never touches a connection the application does not own, and backs
    /// off so a flapping link cannot loop.
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns><see langword="true"/> when a tunnel was reconnected.</returns>
    public async Task<bool> ReconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_physicalLink is null)
        {
            return false;
        }

        var link = _physicalLink.Read();

        // A default route that points at one of our own interfaces is the tunnel
        // itself, not the physical link; ignore it so bringing the tunnel up does not
        // look like a network change.
        if (link is not null && IsTunnelInterface(link.Interface))
        {
            link = null;
        }

        var key = link?.Key;

        if (!_linkKnown)
        {
            _linkKey = key;
            _linkKnown = true;
            return false;
        }

        var previous = _linkKey;

        var owned = _lastSummaries?
            .Where(row => row.Source == TunnelSource.Configuration && row.State == TunnelState.Up)
            .ToArray() ?? [];

        if (!ReconnectPolicy.ShouldReconnect(previous, key, owned.Length > 0))
        {
            _linkKey = key;
            return false;
        }

        var now = _timeProvider.GetUtcNow();
        if (_lastReconnectAt is { } last && now - last < ReconnectBackoff)
        {
            // Keep the previous key so the change is retried once the backoff ends.
            return false;
        }

        _linkKey = key;
        _lastReconnectAt = now;

        foreach (var row in owned)
        {
            _ = await _service.StopAsync(row.Name, cancellationToken).ConfigureAwait(true);
            _ = await _service.StartAsync(row.Name, cancellationToken).ConfigureAwait(true);
        }

        Journal("reconnected after a network change");
        await RefreshAsync().ConfigureAwait(true);
        return true;
    }

    private bool IsTunnelInterface(string interfaceName) =>
        _lastSummaries is not null
        && _lastSummaries.Any(row => string.Equals(_service.ResolveInterfaceName(row.Name), interfaceName, StringComparison.Ordinal));

    /// <summary>Records the operator's selection and resets the transfer display.</summary>
    /// <param name="name">The tunnel name.</param>
    /// <returns>A completed task.</returns>
    public async Task SelectAsync(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        _selectedName = name;
        var summary = _lastSummaries?.FirstOrDefault(row => string.Equals(row.Name, name, StringComparison.Ordinal));
        if (summary is null || summary.Source == TunnelSource.NetworkManager)
        {
            // A connection the application does not own: show its state and the
            // connect action, but no editable configuration.
            View.HideEditor();
            View.HideRename();
            View.HidePolicy();
            return;
        }

        await BeginEditAsync(name).ConfigureAwait(true);
        var policy = _service.GetPolicy(name);
        View.ShowRename(new TunnelRenameSnapshot(name, summary.DisplayName ?? name));
        View.ShowPolicy(new TunnelPolicySnapshot(name, policy.AutoReconnect, policy.DnsMode, policy.SearchDomains));
    }

    /// <summary>
    /// Samples every listed interface's kernel counters and updates the real-time
    /// rate on each row, plus the selected tunnel's full transfer for its detail
    /// page. The counters are cumulative and world-readable, so a poll needs no
    /// privilege; a rate is the difference between two samples over the elapsed
    /// time. The first sample of an interface reports a zero rate.
    /// </summary>
    public void PollTransfer()
    {
        var transfers = new Dictionary<string, TransferView>(StringComparer.Ordinal);

        if (_counterReader is not null)
        {
            var now = _timeProvider.GetUtcNow();

            foreach (var name in TunnelNames())
            {
                var counters = _counterReader.Read(_service.ResolveInterfaceName(name));
                if (!counters.TryGetValue(out var sample))
                {
                    // The interface is down or unknown: the tunnel carries nothing,
                    // so show zero and drop the sample, or the next connection would
                    // start its rate from a stale total.
                    _transferSamples.Remove(name);
                    transfers[name] = ZeroTransfer;
                    continue;
                }

                var (download, upload) = Rates(name, sample, now);
                transfers[name] = new TransferView(
                    string.Concat(FormatBytes(download), "/s"),
                    FormatBytes(sample.ReceivedBytes),
                    string.Concat(FormatBytes(upload), "/s"),
                    FormatBytes(sample.SentBytes));
            }
        }

        View.ShowTunnelTransfers(transfers);
    }

    private IEnumerable<string> TunnelNames()
    {
        if (_selectedName is not null)
        {
            yield return _selectedName;
        }

        if (_lastSummaries is null)
        {
            yield break;
        }

        foreach (var summary in _lastSummaries)
        {
            if (!string.Equals(summary.Name, _selectedName, StringComparison.Ordinal))
            {
                yield return summary.Name;
            }
        }
    }

    private (long Download, long Upload) Rates(string name, NetworkCounters sample, DateTimeOffset now)
    {
        if (!_transferSamples.TryGetValue(name, out var previous))
        {
            _transferSamples[name] = new TransferSample(sample.ReceivedBytes, sample.SentBytes, now);
            return (0, 0);
        }

        var seconds = (now - previous.At).TotalSeconds;
        var download = seconds > 0 ? (long)Math.Max(0, (sample.ReceivedBytes - previous.Received) / seconds) : 0;
        var upload = seconds > 0 ? (long)Math.Max(0, (sample.SentBytes - previous.Sent) / seconds) : 0;

        _transferSamples[name] = new TransferSample(sample.ReceivedBytes, sample.SentBytes, now);
        return (download, upload);
    }

    private static string FormatBytes(long bytes) => ByteSize.FromBytes(bytes).ToHumanString();

    private readonly record struct TransferSample(long Received, long Sent, DateTimeOffset At);

    /// <summary>Opens a blank editor.</summary>
    public void BeginCreate()
    {
        _editor = new EditorSession { IsNew = true, Name = string.Empty };
        _selectedName = null;
        View.SetError(null);
        View.HideRename();
        View.HidePolicy();
        View.ShowEditor(CreateSnapshot(_editor));
        View.SetStatus("New tunnel: fill in the fields and save.");
    }

    /// <summary>Opens the editor for an existing tunnel.</summary>
    /// <param name="name">The tunnel name.</param>
    /// <returns>A task that completes when the view has been updated.</returns>
    public async Task BeginEditAsync(string name)
    {
        var loaded = await Task.Run(() => _service.LoadConfiguration(name)).ConfigureAwait(true);
        if (!loaded.TryGetValue(out var configuration))
        {
            View.SetError(loaded.Error.Message);
            return;
        }

        var session = new EditorSession
        {
            IsNew = false,
            Name = configuration.Name,
            PendingPrivateKey = configuration.Interface.PrivateKey,
            Addresses = string.Join('\n', configuration.Interface.Addresses),
            Dns = string.Join('\n', configuration.Interface.Dns),
            ListenPort = Format(configuration.Interface.ListenPort),
            Mtu = Format(configuration.Interface.Mtu),
            Table = configuration.Interface.Table ?? string.Empty,
            RoutingMode = RoutingModeConvergence.Detect(configuration),
            PreUp = configuration.Interface.PreUp,
            PostUp = configuration.Interface.PostUp,
            PreDown = configuration.Interface.PreDown,
            PostDown = configuration.Interface.PostDown,
        };

        foreach (var peer in configuration.Peers)
        {
            session.Peers.Add(new SessionPeer(ToEditorPeer(peer), peer.PresharedKey));
        }

        _editor = session;
        View.SetError(null);
        View.ShowEditor(CreateSnapshot(session));

        if (!session.PendingPrivateKey.IsEmpty)
        {
            var publicKey = await _service.DerivePublicKeyAsync(session.PendingPrivateKey).ConfigureAwait(true);
            if (publicKey.TryGetValue(out var derived) && ReferenceEquals(_editor, session))
            {
                session.PublicKey = derived;
                View.UpdateEditorKeys(true, derived);
            }
        }
    }

    /// <summary>
    /// Opens a new-tunnel editor pre-filled from pasted profile text (a
    /// configuration the operator copied from their provider). The declared
    /// <c># Name</c> is used as the default name; the private key is held by the
    /// presenter and never rendered.
    /// </summary>
    /// <param name="profileText">The pasted configuration text.</param>
    /// <returns>A task that completes when the view has been updated.</returns>
    public async Task BeginCreateFromTextAsync(string profileText)
    {
        ArgumentNullException.ThrowIfNull(profileText);

        var name = WireGuardConfigParser.ReadDeclaredName(profileText) ?? string.Empty;
        var parsed = WireGuardConfigParser.Parse(name, profileText);
        if (!parsed.TryGetValue(out var configuration))
        {
            View.SetError(parsed.Error.Message);
            return;
        }

        var session = new EditorSession
        {
            IsNew = true,
            Name = configuration.Name,
            PendingPrivateKey = configuration.Interface.PrivateKey,
            Addresses = string.Join('\n', configuration.Interface.Addresses),
            Dns = string.Join('\n', configuration.Interface.Dns),
            ListenPort = Format(configuration.Interface.ListenPort),
            Mtu = Format(configuration.Interface.Mtu),
            Table = configuration.Interface.Table ?? string.Empty,
            RoutingMode = RoutingModeConvergence.Detect(configuration),
            PreUp = configuration.Interface.PreUp,
            PostUp = configuration.Interface.PostUp,
            PreDown = configuration.Interface.PreDown,
            PostDown = configuration.Interface.PostDown,
        };

        foreach (var peer in configuration.Peers)
        {
            session.Peers.Add(new SessionPeer(ToEditorPeer(peer), peer.PresharedKey));
        }

        _editor = session;
        View.SetError(null);
        View.ShowEditor(CreateSnapshot(session));
        View.SetStatus("Review the pasted profile, give it a name and save.");

        if (!session.PendingPrivateKey.IsEmpty)
        {
            var publicKey = await _service.DerivePublicKeyAsync(session.PendingPrivateKey).ConfigureAwait(true);
            if (publicKey.TryGetValue(out var derived) && ReferenceEquals(_editor, session))
            {
                session.PublicKey = derived;
                View.UpdateEditorKeys(true, derived);
            }
        }
    }

    /// <summary>Saves the open editor.</summary>
    /// <param name="input">The text the operator entered.</param>
    /// <returns>A task that completes when the view has been updated.</returns>
    public async Task SaveAsync(EditorInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var session = _editor;
        if (session is null)
        {
            return;
        }

        session.Name = input.Name.Trim();
        session.Addresses = input.Addresses;
        session.Dns = input.Dns;
        session.ListenPort = input.ListenPort;
        session.Mtu = input.Mtu;
        session.Table = input.Table;
        session.RoutingMode = input.RoutingMode;

        var typedKey = input.PrivateKey.Trim();
        if (typedKey.Length > 0)
        {
            session.PendingPrivateKey = SecretKey.FromValue(typedKey);
        }

        var resolved = MergePeers(session, input.Peers);

        var mapped = EditorInputMapper.ToConfiguration(input, session.PendingPrivateKey, resolved);
        if (!mapped.TryGetValue(out var configuration))
        {
            View.SetEditorError(EditorField.FromErrorCode(mapped.Error.Code), mapped.Error.Message);
            return;
        }

        // The editor does not show the wg-quick hooks; preserve the stored ones so a
        // save never drops a line the operator did not edit.
        configuration = configuration with
        {
            Interface = configuration.Interface with
            {
                PreUp = session.PreUp,
                PostUp = session.PostUp,
                PreDown = session.PreDown,
                PostDown = session.PostDown,
            },
        };

        // The mode is the authority over the routing directives: full pins the
        // default route, the table and the fwmark; split clears the two directives
        // and refuses to leave a default route in place.
        var converged = RoutingModeConvergence.Apply(configuration, input.RoutingMode);
        if (!converged.TryGetValue(out configuration))
        {
            View.SetEditorError(EditorField.FromErrorCode(converged.Error.Code), converged.Error.Message);
            return;
        }

        var saved = await Task.Run(() => session.IsNew ? _service.Create(configuration) : _service.Update(configuration)).ConfigureAwait(true);
        if (!saved.TryGetValue(out _))
        {
            View.SetEditorError(EditorField.FromErrorCode(saved.Error.Code), saved.Error.Message);
            return;
        }

        _editor = null;
        _selectedName = configuration.Name;
        View.HideEditor();
        View.SetError(null);
        View.ShowToast("Profile saved.");
        Journal($"saved {configuration.Name}");
        await RefreshAsync().ConfigureAwait(true);
        await SelectAsync(configuration.Name).ConfigureAwait(true);
    }

    /// <summary>Closes the editor without saving.</summary>
    public void CancelEditor()
    {
        _editor = null;
        View.HideEditor();
        View.SetStatus("Edit cancelled.");

        if (_selectedName is { } name)
        {
            _ = SelectAsync(name);
        }
    }

    /// <summary>
    /// Stores the display name the operator typed: the NetworkManager connection
    /// id, or an empty text to restore the real name.
    /// </summary>
    /// <param name="input">The real tunnel name and the display name.</param>
    /// <returns>A task that completes when the view has been updated.</returns>
    public async Task RenameAsync(TunnelRenameInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var renamed = await _service.SetDisplayName(input.Name, input.DisplayName).ConfigureAwait(true);
        if (!renamed.TryGetValue(out var shown))
        {
            View.SetError(renamed.Error.Message);
            return;
        }

        View.HideRename();
        View.SetError(null);
        View.ShowToast(string.Equals(shown, input.Name, StringComparison.Ordinal)
            ? $"Display name cleared for '{input.Name}'."
            : $"Tunnel '{input.Name}' is shown as '{shown}'.");
        Journal($"renamed {input.Name}");
        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Applies a policy change from the inline card. It is quiet: the control the
    /// operator touched already shows the new value, and a toast per change would
    /// be noise.
    /// </summary>
    /// <param name="input">The values read from the card.</param>
    /// <returns>A task that completes when the policy is applied.</returns>
    public async Task ApplyPolicyAsync(TunnelPolicyInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var saved = await _service
            .SetPolicyAsync(input.Name, new TunnelPolicy(input.AutoReconnect, input.DnsMode, input.SearchDomains))
            .ConfigureAwait(true);
        if (!saved.IsSuccess)
        {
            View.SetError(saved.Error.Message);
            return;
        }

        View.SetError(null);
        Journal($"policy {input.Name} dns={input.DnsMode} reconnect={input.AutoReconnect}");
    }

    /// <summary>Adds an empty peer row to the open editor.</summary>
    public void AddPeer() => AddPeer(new EditorPeerInput(
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty));

    /// <summary>Adds a peer row to the open editor.</summary>
    /// <param name="peer">The row to add.</param>
    public void AddPeer(EditorPeerInput peer)
    {
        ArgumentNullException.ThrowIfNull(peer);

        if (_editor is null)
        {
            return;
        }

        _editor.Peers.Add(new SessionPeer(peer, null));
        View.AppendEditorPeerRow(peer);
    }

    /// <summary>Removes a peer row from the open editor.</summary>
    /// <param name="index">The row index.</param>
    public void RemovePeer(int index)
    {
        if (_editor is null || index < 0 || index >= _editor.Peers.Count)
        {
            return;
        }

        _editor.Peers.RemoveAt(index);
        View.RemoveEditorPeerRow(index);
    }

    /// <summary>
    /// Generates a private key and its public key. Only the public key is
    /// pushed to the view; the private half stays in the presenter until save.
    /// </summary>
    /// <returns>A task that completes when the view has been updated.</returns>
    public async Task GenerateKeyPairAsync()
    {
        var session = _editor;
        if (session is null)
        {
            return;
        }

        var generated = await _service.GeneratePrivateKeyAsync().ConfigureAwait(true);
        if (!generated.TryGetValue(out var privateKey))
        {
            View.SetEditorError(EditorField.General, generated.Error.Message);
            return;
        }

        session.PendingPrivateKey = privateKey;

        var publicKey = await _service.DerivePublicKeyAsync(privateKey).ConfigureAwait(true);
        session.PublicKey = publicKey.TryGetValue(out var derived) ? derived : null;

        if (ReferenceEquals(_editor, session))
        {
            View.SetEditorError(EditorField.General, string.Empty);
            View.UpdateEditorKeys(true, session.PublicKey);
            View.SetStatus("Generated a key pair. The private key is held by the editor and is never shown.");
        }
    }

    /// <summary>Deletes a tunnel's configuration.</summary>
    /// <param name="name">The tunnel name.</param>
    /// <returns>A task that completes when the view has been updated.</returns>
    public async Task DeleteAsync(string name)
    {
        var deleted = await Task.Run(() => _service.DeleteAsync(name)).ConfigureAwait(true);
        if (!deleted.TryGetValue(out _))
        {
            View.SetError(deleted.Error.Message);
            return;
        }

        View.SetError(null);
        View.ShowToast("Profile deleted.");
        Journal($"deleted {name}");
        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>Connects a tunnel and notifies the desktop on success.</summary>
    /// <param name="name">The tunnel name.</param>
    /// <returns>A task that completes when the view has been updated.</returns>
    public async Task StartAsync(string name)
    {
        var started = await _service.StartAsync(name).ConfigureAwait(true);
        if (!started.TryGetValue(out var value))
        {
            View.SetError(started.Error.Message);
            return;
        }

        var dns = await _service.DescribeDnsAsync(name).ConfigureAwait(true);
        var dnsNote = dns.TryGetValue(out var dnsState)
            ? string.Concat(" DNS: ", DnsContainmentPolicy.Describe(DnsContainmentPolicy.Classify(dnsState)), ".")
            : string.Empty;
        var warning = FullTunnelWarning(name);

        View.SetError(null);
        View.ShowToast(string.Concat("Tunnel '", value, "' is connected.", dnsNote, warning));
        Journal($"connected {value}");
        await RefreshAsync().ConfigureAwait(true);
    }

    // A full tunnel to a remote endpoint needs the reverse-path mark; warn once, never
    // block, and only for a full tunnel (a split tunnel is unaffected).
    private string FullTunnelWarning(string name)
    {
        if (_routingPrerequisite?.Read() is not { IsSatisfied: false } prerequisite)
        {
            return string.Empty;
        }

        var loaded = _service.LoadConfiguration(name);
        if (!loaded.TryGetValue(out var configuration)
            || RoutingModeConvergence.Detect(configuration) != TunnelRoutingMode.Full)
        {
            return string.Empty;
        }

        return string.Concat(" Warning: the full-tunnel sysctl is missing (", prerequisite.Remediation, ").");
    }

    /// <summary>Disconnects a tunnel and notifies the desktop on success.</summary>
    /// <param name="name">The tunnel name.</param>
    /// <returns>A task that completes when the view has been updated.</returns>
    public async Task StopAsync(string name)
    {
        var stopped = await _service.StopAsync(name).ConfigureAwait(true);
        if (!stopped.TryGetValue(out var value))
        {
            View.SetError(stopped.Error.Message);
            return;
        }

        View.SetError(null);
        View.ShowToast($"Tunnel '{value}' is disconnected.");
        Journal($"disconnected {value}");
        await RefreshAsync().ConfigureAwait(true);
    }

    private async Task CheckConnectivityAsync(CancellationToken cancellationToken)
    {
        if (_connectivity is null || _operationsInFlight > 0)
        {
            return;
        }

        var current = await _connectivity.ReadAsync(cancellationToken).ConfigureAwait(true);
        if (current == NetworkConnectivity.Unknown)
        {
            return;
        }

        var previous = _lastConnectivity;
        if (!_connectivityKnown)
        {
            _connectivityKnown = true;
            _lastConnectivity = current;
            return;
        }

        if (current == previous)
        {
            return;
        }

        _lastConnectivity = current;
        Journal($"network connectivity {previous} -> {current}");

        var tunnelUp = _lastSummaries?.Any(row => row.Source == TunnelSource.Configuration && row.State == TunnelState.Up) ?? false;
        if (!tunnelUp)
        {
            return;
        }

        if (current is NetworkConnectivity.None or NetworkConnectivity.Limited)
        {
            await NotifyAsync("Network connection lost", "The machine cannot reach the internet.").ConfigureAwait(true);
        }
        else if (previous is NetworkConnectivity.None or NetworkConnectivity.Limited && current == NetworkConnectivity.Full)
        {
            await NotifyAsync("Network connection restored", "The machine is online again.").ConfigureAwait(true);
        }
    }

    private void NotifyTransitions(IReadOnlyList<TunnelSummary> previous, IReadOnlyList<TunnelSummary> current)
    {
        foreach (var row in current)
        {
            if (row.Source != TunnelSource.Configuration)
            {
                continue;
            }

            var before = previous.FirstOrDefault(candidate => string.Equals(candidate.Name, row.Name, StringComparison.Ordinal));
            if (before is null || before.Source != TunnelSource.Configuration || before.State == row.State)
            {
                continue;
            }

            if (before.State == TunnelState.Up && row.State == TunnelState.Down)
            {
                Journal($"noticed {row.Name} went down");
                _ = NotifyAsync("VPN connection lost", $"'{row.Name}' went down.");
            }
            else if (before.State == TunnelState.Down && row.State == TunnelState.Up)
            {
                Journal($"noticed {row.Name} is back up");
                _ = NotifyAsync("VPN reconnected", $"'{row.Name}' is back up.");
            }
        }
    }

    private async Task NotifyAsync(string title, string body)
    {
        if (_notifier is null)
        {
            return;
        }

        try
        {
            await _notifier.NotifyAsync(title, body).ConfigureAwait(true);
        }
        catch (Exception)
        {
            // A desktop notification is best effort; the list already shows the state.
        }
    }

    /// <summary>
    /// Asks what to do about the connected tunnels before the window closes.
    /// Closing disconnects the VPN: once the window is gone nothing would manage
    /// it.
    /// </summary>
    /// <returns><see langword="true"/> when the application may close.</returns>
    public async Task<bool> ConfirmCloseAsync()
    {
        var tunnels = await _service.ListTunnelsAsync().ConfigureAwait(true);
        var connected = tunnels.TryGetValue(out var rows)
            ? rows.Where(row => row.State == TunnelState.Up).Select(row => row.Name).ToArray()
            : [];

        if (connected.Length == 0)
        {
            return true;
        }

        View.ShowWindow();
        if (!await View.ConfirmCloseAsync(connected).ConfigureAwait(true))
        {
            return false;
        }

        foreach (var name in connected)
        {
            _ = await _service.StopAsync(name).ConfigureAwait(true);
        }

        return true;
    }

    /// <summary>Imports a configuration file as a tunnel named after the file stem.</summary>
    /// <param name="sourcePath">The file the operator chose.</param>
    /// <returns>A task that completes when the view has been updated.</returns>
    public async Task ImportAsync(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var imported = await Task.Run(() => _service.Import(name, sourcePath)).ConfigureAwait(true);
        if (!imported.TryGetValue(out _))
        {
            View.SetError(imported.Error.Message);
            return;
        }

        _selectedName = name;
        View.SetError(null);
        View.ShowToast($"Imported as '{name}'.");
        Journal($"imported {name}");
        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>Exports the selected tunnel's configuration to a file.</summary>
    /// <param name="destinationPath">The destination the operator chose.</param>
    /// <returns>A task that completes when the view has been updated.</returns>
    public async Task ExportAsync(string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        if (_selectedName is null)
        {
            View.SetError("Select a tunnel before exporting.");
            return;
        }

        var exported = await Task.Run(() => _service.Export(_selectedName, destinationPath)).ConfigureAwait(true);
        if (!exported.TryGetValue(out _))
        {
            View.SetError(exported.Error.Message);
            return;
        }

        View.SetError(null);
        View.ShowToast($"Exported '{_selectedName}'.");
        Journal($"exported {_selectedName}");
    }

    private static List<SecretKey?> MergePeers(EditorSession session, IReadOnlyList<EditorPeerInput> inputs)
    {
        var merged = new List<SessionPeer>(inputs.Count);
        var resolved = new List<SecretKey?>(inputs.Count);

        for (var index = 0; index < inputs.Count; index++)
        {
            var peer = inputs[index];
            var previous = index < session.Peers.Count ? session.Peers[index] : null;
            var typed = peer.PresharedKey.Trim();
            var presharedKey = typed.Length > 0 ? SecretKey.FromValue(typed) : previous?.PresharedKey;

            merged.Add(new SessionPeer(peer, presharedKey));
            resolved.Add(presharedKey);
        }

        session.Peers.Clear();
        session.Peers.AddRange(merged);
        return resolved;
    }

    private static EditorSnapshot CreateSnapshot(EditorSession session) => new(
        session.IsNew ? "New tunnel" : $"Edit '{session.Name}'",
        session.IsNew,
        session.Name,
        session.IsNew,
        session.Addresses,
        session.Dns,
        session.ListenPort,
        session.Mtu,
        session.Table,
        session.RoutingMode,
        !session.PendingPrivateKey.IsEmpty,
        session.PublicKey,
        session.Peers.Select(peer => peer.Input).ToArray());

    private void RunGuarded(Func<Task> operation) => PendingOperation = GuardAsync(operation);

    private async Task GuardAsync(Func<Task> operation)
    {
        _operationsInFlight++;
        try
        {
            await operation().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            View.SetError($"Unexpected failure: {exception.Message}");
            View.SetBusy(false);
        }
        finally
        {
            _operationsInFlight--;
        }
    }

    private TunnelListItem ToListItem(TunnelSummary summary) => new(
        summary.Name,
        summary.State switch
        {
            TunnelState.Up => "Up",
            TunnelState.Down => "Down",
            _ => "Unknown",
        },
        summary.PeerCount switch
        {
            0 => "no peers",
            1 => "1 peer",
            _ => string.Create(CultureInfo.InvariantCulture, $"{summary.PeerCount} peers"),
        },
        summary.Source == TunnelSource.NetworkManager
            ? "NetworkManager"
            : summary.HasConfiguration ? "configured" : "runtime only",
        summary.Note,
        summary.Source,
        summary.DisplayName);

    private static EditorPeerInput ToEditorPeer(PeerConfiguration peer) => new(
        peer.Label ?? string.Empty,
        peer.PublicKey,
        string.Empty,
        string.Join('\n', peer.AllowedIPs),
        peer.Endpoint ?? string.Empty,
        Format(peer.PersistentKeepalive));

    private static string Format(int? value, string fallback = "") =>
        value is { } number ? number.ToString(CultureInfo.InvariantCulture) : fallback;

    private sealed record SessionPeer(EditorPeerInput Input, SecretKey? PresharedKey);

    private sealed class EditorSession
    {
        internal bool IsNew { get; init; }

        internal string Name { get; set; } = string.Empty;

        internal SecretKey PendingPrivateKey { get; set; } = SecretKey.Empty;

        internal string? PublicKey { get; set; }

        internal string Addresses { get; set; } = string.Empty;

        internal string Dns { get; set; } = string.Empty;

        internal string ListenPort { get; set; } = string.Empty;

        internal string Mtu { get; set; } = string.Empty;

        internal string Table { get; set; } = string.Empty;

        internal TunnelRoutingMode RoutingMode { get; set; } = TunnelRoutingMode.Split;

        internal IReadOnlyList<string> PreUp { get; init; } = [];

        internal IReadOnlyList<string> PostUp { get; init; } = [];

        internal IReadOnlyList<string> PreDown { get; init; } = [];

        internal IReadOnlyList<string> PostDown { get; init; } = [];

        internal List<SessionPeer> Peers { get; } = [];
    }
}
