namespace VpnManager.Core.Presentation;

/// <summary>
/// Decides when a full tunnel must be re-established after the physical link moved.
/// It acts only on a real link change, never on idle traffic, so a quiet tunnel is
/// left alone and a foreign connection is never touched.
/// </summary>
public static class ReconnectPolicy
{
    /// <summary>Decides whether an owned tunnel must be reconnected.</summary>
    /// <param name="previousKey">The link key of the previous observation, or <see langword="null"/> when the machine had no link.</param>
    /// <param name="currentKey">The link key now, or <see langword="null"/> when the machine has no usable link.</param>
    /// <param name="ownedTunnelUp">Whether a tunnel the application owns is currently up.</param>
    /// <returns><see langword="true"/> when the tunnel must be re-established.</returns>
    public static bool ShouldReconnect(string? previousKey, string? currentKey, bool ownedTunnelUp)
    {
        if (!ownedTunnelUp || currentKey is null)
        {
            // Nothing to reconnect, or the network is not back yet: wait.
            return false;
        }

        return !string.Equals(previousKey, currentKey, StringComparison.Ordinal);
    }
}
