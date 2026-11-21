using Core.Mvp.Views;

namespace VpnManager.Core.Presentation;

/// <summary>
/// The main window as the presenter sees it. Intent travels in as semantic
/// events carrying the data the operator entered; state travels out through the
/// Show/Set calls. The implementation holds no logic.
/// </summary>
/// <remarks>
/// <para>
/// The interface is deliberately free of Avalonia types, so the presenter and
/// its tests run with no display. It is a VpnManager passive view
/// (<see cref="IView"/>): the shared base already carries the busy flag and the
/// failure presentation, so only the tunnel-specific surface is added here.
/// </para>
/// </remarks>
public interface ITunnelListView : IView
{
    /// <summary>The operator asked to re-read the tunnel list.</summary>
    event Action RefreshRequested;

    /// <summary>The operator selected a tunnel in the list.</summary>
    event Action<string> TunnelSelected;

    /// <summary>The operator asked for a new tunnel editor.</summary>
    event Action CreateRequested;

    /// <summary>The operator asked to edit a tunnel.</summary>
    event Action<string> EditRequested;

    /// <summary>The operator asked to delete a tunnel.</summary>
    event Action<string> DeleteRequested;

    /// <summary>The operator asked to rename (set a display name for) a tunnel.</summary>
    event Action<string> RenameRequested;

    /// <summary>The operator submitted a display name for a tunnel.</summary>
    event Action<TunnelRenameInput> RenameSubmitted;

    /// <summary>The operator dismissed the rename page.</summary>
    event Action RenameCancelRequested;

    /// <summary>The operator asked to connect a tunnel.</summary>
    event Action<string> StartRequested;

    /// <summary>The operator asked to disconnect a tunnel.</summary>
    event Action<string> StopRequested;

    /// <summary>The operator asked to choose a file to import.</summary>
    event Action<string> ImportRequested;

    /// <summary>The operator asked to create a tunnel from profile text (a pasted configuration).</summary>
    event Action<string> ProfileTextPasted;

    /// <summary>The operator chose a destination file to export to.</summary>
    event Action<string> ExportRequested;

    /// <summary>The operator asked to save the open editor.</summary>
    event Action<EditorInput> SaveRequested;

    /// <summary>The operator dismissed the editor.</summary>
    event Action EditorCancelRequested;

    /// <summary>The operator asked to add a peer row.</summary>
    event Action EditorPeerAddRequested;

    /// <summary>The operator asked to remove the peer row at the index.</summary>
    event Action<int> EditorPeerRemoveRequested;

    /// <summary>The operator asked to generate a key pair for the editor.</summary>
    event Action EditorKeyPairRequested;

    /// <summary>Replaces the tunnel list.</summary>
    /// <param name="tunnels">The rows.</param>
    /// <param name="selectedName">The tunnel to keep selected, or <see langword="null"/>.</param>
    void ShowTunnels(IReadOnlyList<TunnelListItem> tunnels, string? selectedName);

    /// <summary>Replaces the real-time transfer of the selected tunnel (the detail page).</summary>
    /// <param name="transfer">The formatted rates and totals.</param>
    void ShowTransfer(TransferView transfer);

    /// <summary>
    /// Replaces the real-time rate shown on each tunnel row of the master list.
    /// </summary>
    /// <param name="transfers">The formatted transfer per tunnel name; a name absent from the map keeps its last value.</param>
    void ShowTunnelTransfers(IReadOnlyDictionary<string, TransferView> transfers);

    /// <summary>Opens the editor and sets every field from the snapshot.</summary>
    /// <param name="editor">The editor state; it contains no private key.</param>
    void ShowEditor(EditorSnapshot editor);

    /// <summary>Opens the rename page for a tunnel.</summary>
    /// <param name="snapshot">The real name and the name currently shown.</param>
    void ShowRename(TunnelRenameSnapshot snapshot);

    /// <summary>Closes the rename page.</summary>
    void HideRename();

    /// <summary>
    /// Updates only the key identity line, so a key generation does not overwrite
    /// text the operator is still editing.
    /// </summary>
    /// <param name="hasPrivateKey">Whether the presenter now holds a private key.</param>
    /// <param name="publicKey">The derived public key, or <see langword="null"/>.</param>
    void UpdateEditorKeys(bool hasPrivateKey, string? publicKey);

    /// <summary>Appends a peer row to the open editor.</summary>
    /// <param name="peer">The row to initialize.</param>
    void AppendEditorPeerRow(EditorPeerInput peer);

    /// <summary>Removes the peer row at the index from the open editor.</summary>
    /// <param name="index">The row index.</param>
    void RemoveEditorPeerRow(int index);

    /// <summary>Closes the editor.</summary>
    void HideEditor();

    /// <summary>Marks the offending field and shows the message.</summary>
    /// <param name="fieldKey">One of the <see cref="EditorField"/> constants.</param>
    /// <param name="message">The actionable message; an empty message clears the marker.</param>
    void SetEditorError(string fieldKey, string message);

    /// <summary>Shows a status line.</summary>
    /// <param name="message">The message.</param>
    void SetStatus(string message);

    /// <summary>Shows a transient success message; it disappears on its own.</summary>
    /// <param name="message">The message.</param>
    void ShowToast(string message);

    /// <summary>Shows an error line, or clears it with <see langword="null"/>.</summary>
    /// <param name="message">The message, or <see langword="null"/>.</param>
    void SetError(string? message);

    /// <summary>
    /// Asks the operator what to do about the tunnels still connected before the
    /// application exits.
    /// </summary>
    /// <param name="connectedNames">The connected tunnel names.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The operator's answer.</returns>
    Task<QuitChoice> ConfirmQuitAsync(IReadOnlyList<string> connectedNames, CancellationToken cancellationToken = default);

    /// <summary>Shows and focuses the main window, so a dialog cannot stay hidden.</summary>
    void ShowWindow();
}
