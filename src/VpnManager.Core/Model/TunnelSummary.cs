namespace VpnManager.Core.Model;

/// <summary>
/// One row of the tunnel list: what is configured on disk merged with what the
/// kernel currently reports.
/// </summary>
/// <param name="Name">The tunnel name (configuration file stem or interface name).</param>
/// <param name="State">The observed runtime state.</param>
/// <param name="HasConfiguration">Whether a configuration file exists for the name.</param>
/// <param name="PeerCount">The number of peers, from the runtime when available, otherwise from the configuration.</param>
/// <param name="ListenPort">The listening port when known.</param>
/// <param name="Note">An optional explanation when part of the row could not be determined.</param>
/// <param name="Source">Whether the tunnel is a wg-quick file or a NetworkManager connection.</param>
/// <param name="DisplayName">The operator's display name for the row, or <see langword="null"/> to show <paramref name="Name"/>.</param>
public sealed record TunnelSummary(
    string Name,
    TunnelState State,
    bool HasConfiguration,
    int PeerCount,
    int? ListenPort,
    string? Note = null,
    TunnelSource Source = TunnelSource.Configuration,
    string? DisplayName = null);
