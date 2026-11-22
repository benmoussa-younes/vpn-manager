using VpnManager.Core.NetworkManager;
using VpnManager.Core.State;

namespace VpnManager.Core.Tests;

/// <summary>
/// The stored policy outlives a mode that no longer exists: the retired
/// full-tunnel mode was stored as <c>3</c>.
/// </summary>
public sealed class TunnelStateEntryTests
{
    [Fact]
    public void A_retired_dns_mode_reads_as_profile()
    {
        var entry = new TunnelStateEntry { DnsMode = (NetworkManagerDnsMode)3 };

        Assert.Equal(NetworkManagerDnsMode.Profile, entry.DnsMode);
    }

    [Fact]
    public void A_known_dns_mode_is_kept()
    {
        var entry = new TunnelStateEntry { DnsMode = NetworkManagerDnsMode.Split };

        Assert.Equal(NetworkManagerDnsMode.Split, entry.DnsMode);
    }
}
