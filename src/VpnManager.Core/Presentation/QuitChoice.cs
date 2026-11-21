namespace VpnManager.Core.Presentation;

/// <summary>
/// The operator's answer when quitting while a tunnel is still connected.
/// </summary>
public enum QuitChoice
{
    /// <summary>Disconnect the connected tunnels, then quit.</summary>
    DisconnectAndQuit,

    /// <summary>Leave the tunnels connected and quit.</summary>
    StayConnectedAndQuit,

    /// <summary>Do not quit.</summary>
    Cancel,
}
