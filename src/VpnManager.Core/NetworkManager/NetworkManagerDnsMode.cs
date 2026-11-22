namespace VpnManager.Core.NetworkManager;

/// <summary>
/// How a profile's DNS entries reach NetworkManager. The profile's DNS servers are
/// always applied to the tunnel link; the mode decides which names they answer.
/// </summary>
public enum NetworkManagerDnsMode
{
    /// <summary>Use the profile DNS for every name while the tunnel is up.</summary>
    Profile = 0,

    /// <summary>Leave the system resolver untouched and ignore the profile DNS.</summary>
    System = 1,

    /// <summary>Send only the listed domains to the profile DNS; other names keep the system resolver.</summary>
    Split = 2,
}
