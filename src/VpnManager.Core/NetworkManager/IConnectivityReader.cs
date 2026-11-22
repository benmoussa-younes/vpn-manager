namespace VpnManager.Core.NetworkManager;

/// <summary>NetworkManager's view of the machine's internet connectivity.</summary>
public enum NetworkConnectivity
{
    /// <summary>The state could not be read.</summary>
    Unknown = 0,

    /// <summary>No connectivity at all.</summary>
    None = 1,

    /// <summary>The machine reaches the network but not the whole internet.</summary>
    Limited = 2,

    /// <summary>A captive portal answers.</summary>
    Portal = 3,

    /// <summary>Full internet connectivity.</summary>
    Full = 4,
}

/// <summary>
/// The network-connectivity boundary: the signal the window uses to tell the
/// operator that the machine lost its network, which the tunnel state alone does
/// not show (a full tunnel keeps its interface while the link is gone).
/// </summary>
public interface IConnectivityReader
{
    /// <summary>Reads the current connectivity.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The state, or <see cref="NetworkConnectivity.Unknown"/> when it cannot be read.</returns>
    Task<NetworkConnectivity> ReadAsync(CancellationToken cancellationToken = default);
}
