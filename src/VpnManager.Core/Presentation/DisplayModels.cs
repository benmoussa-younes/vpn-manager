namespace VpnManager.Core.Presentation;

using VpnManager.Core.Model;

/// <summary>
/// One row of the tunnel list, already formatted for display. The presenter
/// formats; the view only assigns text.
/// </summary>
/// <param name="Name">The tunnel name.</param>
/// <param name="State">The state text: <c>Up</c>, <c>Down</c> or <c>Unknown</c>.</param>
/// <param name="Peers">The peer-count text.</param>
/// <param name="Configuration">Whether a configuration file exists, as text.</param>
/// <param name="Note">An explanation when part of the row could not be determined.</param>
/// <param name="Source">Whether the tunnel is a wg-quick file or a NetworkManager connection.</param>
/// <param name="DisplayName">The operator's display name for the row, or <see langword="null"/> to show <paramref name="Name"/>.</param>
public sealed record TunnelListItem(
    string Name,
    string State,
    string Peers,
    string Configuration,
    string? Note,
    TunnelSource Source = TunnelSource.Configuration,
    string? DisplayName = null)
{
    /// <summary>Gets the name to render: the operator's alias when set, otherwise the real name.</summary>
    public string ShownName => string.IsNullOrEmpty(DisplayName) ? Name : DisplayName;
}

/// <summary>
/// The rename page's initial state. It carries the real tunnel name (the identity
/// every command uses) and the name to show in the text box (the alias, or the
/// real name when there is none).
/// </summary>
/// <param name="Name">The real tunnel name.</param>
/// <param name="CurrentDisplayName">The name currently shown for the tunnel.</param>
public sealed record TunnelRenameSnapshot(string Name, string CurrentDisplayName);

/// <summary>
/// The display name the operator typed. It is shown only by the application; the
/// tunnel keeps its real name.
/// </summary>
/// <param name="Name">The real tunnel name.</param>
/// <param name="DisplayName">The display name, or empty to show the real name.</param>
public sealed record TunnelRenameInput(string Name, string DisplayName);

/// <summary>
/// The real-time transfer of the selected tunnel, already formatted. The rates are
/// computed from consecutive samples of the kernel counters, which are
/// world-readable, so the display needs no privilege.
/// </summary>
/// <param name="DownloadRate">The received bytes per second, human formatted.</param>
/// <param name="DownloadTotal">The total received bytes, human formatted.</param>
/// <param name="UploadRate">The sent bytes per second, human formatted.</param>
/// <param name="UploadTotal">The total sent bytes, human formatted.</param>
public sealed record TransferView(
    string DownloadRate,
    string DownloadTotal,
    string UploadRate,
    string UploadTotal);
