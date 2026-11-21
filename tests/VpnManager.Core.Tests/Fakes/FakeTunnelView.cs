using Core.Results;
using VpnManager.Core.Presentation;

namespace VpnManager.Core.Tests.Fakes;

/// <summary>
/// A recording view. Every string handed to it is kept so a test can assert what
/// the presenter decided to show — in particular that no private key ever
/// arrives. The events are raised by the <c>Request*</c> helpers.
/// </summary>
internal sealed class FakeTunnelView : ITunnelListView
{
    /// <summary>Every string the view was handed, in order.</summary>
    internal List<string> Strings { get; } = [];

    /// <summary>When set, <see cref="ShowTunnels"/> throws, to exercise the presenter's guard.</summary>
    internal bool ThrowOnShowTunnels { get; set; }

    /// <summary>The last tunnel list shown.</summary>
    internal IReadOnlyList<TunnelListItem> Tunnels { get; private set; } = [];

    /// <summary>How many times the tunnel list was pushed to the view.</summary>
    internal int ShowTunnelsCount { get; private set; }

    /// <summary>The selection the presenter asked to keep.</summary>
    internal string? SelectedName { get; private set; }

    /// <summary>The last real-time transfer shown.</summary>
    internal TransferView? Transfer { get; private set; }

    /// <summary>The last editor snapshot shown.</summary>
    internal EditorSnapshot? Editor { get; private set; }

    /// <summary>Whether the editor is open.</summary>
    internal bool EditorVisible { get; private set; }

    /// <summary>Editor errors, formatted as "field|message".</summary>
    internal List<string> EditorErrors { get; } = [];

    /// <summary>The last status line.</summary>
    internal string? Status { get; private set; }

    /// <summary>Transient success messages shown to the operator.</summary>
    internal List<string> Toasts { get; } = [];

    /// <summary>The last error line, or null.</summary>
    internal string? Error { get; private set; }

    /// <summary>The last busy flag.</summary>
    internal bool Busy { get; private set; }

    /// <summary>Peer rows appended by the presenter.</summary>
    internal List<EditorPeerInput> AppendedPeers { get; } = [];

    /// <summary>Peer row indexes removed by the presenter.</summary>
    internal List<int> RemovedPeerIndexes { get; } = [];

    /// <summary>Key identity updates.</summary>
    internal List<(bool HasPrivateKey, string? PublicKey)> KeyUpdates { get; } = [];


    /// <summary>The last policy snapshot shown.</summary>
    internal TunnelPolicySnapshot? Policy { get; private set; }

    /// <summary>Whether the policy page is open.</summary>
    internal bool PolicyVisible { get; private set; }

    public void ShowPolicy(TunnelPolicySnapshot snapshot)
    {
        Policy = snapshot;
        PolicyVisible = true;
    }

    public void HidePolicy() => PolicyVisible = false;


    /// <summary>The connected-name lists passed to <see cref="ConfirmCloseAsync"/>.</summary>
    internal List<IReadOnlyList<string>> CloseConfirmations { get; } = [];

    /// <summary>The answer <see cref="ConfirmCloseAsync"/> returns.</summary>
    internal bool CloseAnswer { get; set; } = true;

    public Task<bool> ConfirmCloseAsync(IReadOnlyList<string> connectedNames, CancellationToken cancellationToken = default)
    {
        CloseConfirmations.Add(connectedNames);
        return Task.FromResult(CloseAnswer);
    }


    /// <summary>Whether the presenter asked the view to show the window.</summary>
    internal bool WindowShown { get; private set; }

    public void ShowWindow() => WindowShown = true;

    public event Action? RefreshRequested;

    public event Action<string>? PolicyEditRequested;

    public event Action<TunnelPolicyInput>? PolicySubmitted;

    public event Action? PolicyCancelRequested;

    internal void RaisePolicyEdit(string name) => PolicyEditRequested?.Invoke(name);

    internal void RaisePolicySubmitted(TunnelPolicyInput input) => PolicySubmitted?.Invoke(input);

    internal void RaisePolicyCancel() => PolicyCancelRequested?.Invoke();

    public event Action<string>? TunnelSelected;

    public event Action? CreateRequested;

    public event Action<string>? EditRequested;

    public event Action<string>? DeleteRequested;

    public event Action<string>? RenameRequested;

    public event Action<TunnelRenameInput>? RenameSubmitted;

    public event Action? RenameCancelRequested;

    public event Action<string>? StartRequested;

    public event Action<string>? StopRequested;

    public event Action<string>? ImportRequested;

    public event Action<string>? ProfileTextPasted;

    public event Action<string>? ExportRequested;

    public event Action<EditorInput>? SaveRequested;

    public event Action? EditorCancelRequested;

    public event Action? EditorPeerAddRequested;

    public event Action<int>? EditorPeerRemoveRequested;

    public event Action? EditorKeyPairRequested;

    /// <summary>Raises <see cref="RefreshRequested"/>.</summary>
    internal void RequestRefresh() => RefreshRequested?.Invoke();

    /// <summary>Raises <see cref="TunnelSelected"/>.</summary>
    internal void SelectTunnel(string name) => TunnelSelected?.Invoke(name);

    /// <summary>Raises <see cref="CreateRequested"/>.</summary>
    internal void RequestCreate() => CreateRequested?.Invoke();

    /// <summary>Raises <see cref="EditRequested"/>.</summary>
    internal void RequestEdit(string name) => EditRequested?.Invoke(name);

    /// <summary>Raises <see cref="DeleteRequested"/>.</summary>
    internal void RequestDelete(string name) => DeleteRequested?.Invoke(name);

    /// <summary>The tunnel names the test asked to rename.</summary>
    internal List<string> RenameRequests { get; } = [];

    /// <summary>The display names the test submitted.</summary>
    internal List<TunnelRenameInput> RenameSubmissions { get; } = [];

    /// <summary>The last rename snapshot shown.</summary>
    internal TunnelRenameSnapshot? Rename { get; private set; }

    /// <summary>Whether the rename page is open.</summary>
    internal bool RenameVisible { get; private set; }

    /// <summary>Raises <see cref="RenameRequested"/>.</summary>
    internal void RequestRename(string name)
    {
        RenameRequests.Add(name);
        RenameRequested?.Invoke(name);
    }

    /// <summary>Raises <see cref="RenameSubmitted"/>.</summary>
    internal void SubmitRename(TunnelRenameInput input)
    {
        RenameSubmissions.Add(input);
        RenameSubmitted?.Invoke(input);
    }

    /// <summary>Raises <see cref="RenameCancelRequested"/>.</summary>
    internal void RequestRenameCancel() => RenameCancelRequested?.Invoke();

    /// <summary>Raises <see cref="StartRequested"/>.</summary>
    internal void RequestStart(string name) => StartRequested?.Invoke(name);

    /// <summary>Raises <see cref="StopRequested"/>.</summary>
    internal void RequestStop(string name) => StopRequested?.Invoke(name);

    /// <summary>Raises <see cref="ImportRequested"/>.</summary>
    internal void RequestImport(string path) => ImportRequested?.Invoke(path);

    /// <summary>Raises <see cref="ProfileTextPasted"/>.</summary>
    internal void RequestPasteProfile(string text) => ProfileTextPasted?.Invoke(text);

    /// <summary>Raises <see cref="ExportRequested"/>.</summary>
    internal void RequestExport(string path) => ExportRequested?.Invoke(path);

    /// <summary>Raises <see cref="SaveRequested"/>.</summary>
    internal void RequestSave(EditorInput input) => SaveRequested?.Invoke(input);

    /// <summary>Raises <see cref="EditorCancelRequested"/>.</summary>
    internal void RequestCancel() => EditorCancelRequested?.Invoke();

    /// <summary>Raises <see cref="EditorPeerAddRequested"/>.</summary>
    internal void RequestAddPeer() => EditorPeerAddRequested?.Invoke();

    /// <summary>Raises <see cref="EditorPeerRemoveRequested"/>.</summary>
    internal void RequestRemovePeer(int index) => EditorPeerRemoveRequested?.Invoke(index);

    /// <summary>Raises <see cref="EditorKeyPairRequested"/>.</summary>
    internal void RequestKeyPair() => EditorKeyPairRequested?.Invoke();

    /// <summary>Whether any string the view received contains the given text.</summary>
    internal bool ReceivedTextContaining(string text) =>
        Strings.Any(value => value.Contains(text, StringComparison.Ordinal));

    public void ShowTunnels(IReadOnlyList<TunnelListItem> tunnels, string? selectedName)
    {
        if (ThrowOnShowTunnels)
        {
            throw new InvalidOperationException("the view refused the rows");
        }

        ShowTunnelsCount++;
        Tunnels = tunnels;
        SelectedName = selectedName;

        foreach (var tunnel in tunnels)
        {
            Strings.Add(tunnel.Name);
            Strings.Add(tunnel.State);
            Strings.Add(tunnel.Peers);
            Strings.Add(tunnel.Configuration);

            if (tunnel.Note is not null)
            {
                Strings.Add(tunnel.Note);
            }
        }
    }

    public void ShowTransfer(TransferView transfer) => Transfer = transfer;

    public void ShowTunnelTransfers(IReadOnlyDictionary<string, TransferView> transfers) => TunnelTransfers = transfers;

    /// <summary>The last per-row transfer map pushed to the list.</summary>
    internal IReadOnlyDictionary<string, TransferView> TunnelTransfers { get; private set; } =
        new Dictionary<string, TransferView>(StringComparer.Ordinal);

    public void ShowEditor(EditorSnapshot editor)
    {
        Editor = editor;
        EditorVisible = true;
        Strings.Add(editor.Title);
        Strings.Add(editor.Name);
        Strings.Add(editor.Addresses);
        Strings.Add(editor.Dns);
        Strings.Add(editor.ListenPort);
        Strings.Add(editor.Mtu);
        Strings.Add(editor.Table);

        if (editor.PublicKey is not null)
        {
            Strings.Add(editor.PublicKey);
        }

        foreach (var peer in editor.Peers)
        {
            RecordPeer(peer);
        }
    }

    public void UpdateEditorKeys(bool hasPrivateKey, string? publicKey)
    {
        KeyUpdates.Add((hasPrivateKey, publicKey));

        if (publicKey is not null)
        {
            Strings.Add(publicKey);
        }
    }

    public void AppendEditorPeerRow(EditorPeerInput peer)
    {
        AppendedPeers.Add(peer);
        RecordPeer(peer);
    }

    public void RemoveEditorPeerRow(int index) => RemovedPeerIndexes.Add(index);

    public void HideEditor() => EditorVisible = false;

    public void ShowRename(TunnelRenameSnapshot snapshot)
    {
        Rename = snapshot;
        RenameVisible = true;
        Strings.Add(snapshot.Name);
        Strings.Add(snapshot.CurrentDisplayName);
    }

    public void HideRename() => RenameVisible = false;

    public void SetEditorError(string fieldKey, string message) => EditorErrors.Add($"{fieldKey}|{message}");

    public void SetStatus(string message) => Status = message;

    public void ShowToast(string message) => Toasts.Add(message);

    public void SetError(string? message) => Error = message;

    public void ShowError(CoreError error) => Error = error.Message;

    public void SetBusy(bool busy) => Busy = busy;

    private void RecordPeer(EditorPeerInput peer)
    {
        Strings.Add(peer.Label);
        Strings.Add(peer.PublicKey);
        Strings.Add(peer.PresharedKey);
        Strings.Add(peer.AllowedIps);
        Strings.Add(peer.Endpoint);
        Strings.Add(peer.PersistentKeepalive);
    }
}
