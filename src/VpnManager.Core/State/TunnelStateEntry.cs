using System.Text.Json.Serialization;
using VpnManager.Core.Model;
using VpnManager.Core.NetworkManager;

namespace VpnManager.Core.State;

/// <summary>
/// What the application remembers about one tunnel. The configuration file is the
/// truth, but in the system scope it is root-only, so this is the memory that keeps
/// a configured tunnel visible while it is down or unreadable.
/// </summary>
public sealed record TunnelStateEntry
{
    /// <summary>Gets the tunnel name (the configuration file stem).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the scope the tunnel belongs to.</summary>
    public TunnelScope Scope { get; init; }

    /// <summary>Gets how the tunnel entered the application: <c>created</c> or <c>imported</c>.</summary>
    public string Source { get; init; } = "unknown";

    /// <summary>Gets the last observed peer count.</summary>
    public int PeerCount { get; init; }

    /// <summary>Gets the last observed listen port, when known.</summary>
    public int? ListenPort { get; init; }

    /// <summary>Gets when this entry was last written by the application.</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// Gets whether the entry was confirmed against the configuration directory.
    /// An unverified entry is shown as "not verified" and pruned on reconciliation.
    /// </summary>
    public bool Verified { get; init; }

    private readonly NetworkManagerDnsMode _dnsMode = NetworkManagerDnsMode.Profile;

    /// <summary>Gets whether NetworkManager may reconnect the profile after a link change.</summary>
    public bool AutoReconnect { get; init; } = true;

    /// <summary>Gets how the profile's DNS entries are applied.</summary>
    /// <remarks>
    /// A stored value that no longer names a mode (the removed full-tunnel mode
    /// was <c>3</c>) reads as <see cref="NetworkManagerDnsMode.Profile"/>, its
    /// historical behaviour, instead of failing the whole state load.
    /// </remarks>
    [JsonConverter(typeof(NetworkManagerDnsModeJsonConverter))]
    public NetworkManagerDnsMode DnsMode
    {
        get => _dnsMode;
        init => _dnsMode = Enum.IsDefined(value) ? value : NetworkManagerDnsMode.Profile;
    }

    /// <summary>Gets the search domains the split-DNS mode routes through the tunnel.</summary>
    public IReadOnlyList<string> DnsSearchDomains { get; init; } = [];
}
