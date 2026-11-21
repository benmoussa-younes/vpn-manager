namespace VpnManager.Core.NetworkManager;

/// <summary>
/// How a profile's DNS entries reach NetworkManager.
/// </summary>
public enum NetworkManagerDnsMode
{
    /// <summary>Use the DNS servers and search domains from the profile.</summary>
    Profile = 0,

    /// <summary>Leave the system resolver untouched and ignore the profile DNS.</summary>
    System = 1,

    /// <summary>Use the profile DNS for the listed search domains only.</summary>
    Split = 2,

    /// <summary>Use the profile DNS for every name while the tunnel is up.</summary>
    FullTunnel = 3,
}
