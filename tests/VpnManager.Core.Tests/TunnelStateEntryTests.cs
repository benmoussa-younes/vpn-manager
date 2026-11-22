using System.Text.Json;
using VpnManager.Core.NetworkManager;
using VpnManager.Core.State;

namespace VpnManager.Core.Tests;

/// <summary>
/// The stored policy outlives a mode that no longer exists: the retired
/// full-tunnel mode was stored as <c>3</c>.
/// </summary>
public sealed class TunnelStateEntryTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

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

    [Theory]
    [InlineData("{\"name\":\"wg0\",\"dnsMode\":\"FullTunnel\"}")]
    [InlineData("{\"name\":\"wg0\",\"dnsMode\":3}")]
    public void A_stored_document_with_the_retired_mode_still_loads(string json)
    {
        var entry = JsonSerializer.Deserialize<TunnelStateEntry>(json, Json);

        Assert.NotNull(entry);
        Assert.Equal(NetworkManagerDnsMode.Profile, entry.DnsMode);
    }

    [Fact]
    public void A_stored_document_keeps_a_known_mode_and_writes_it_by_name()
    {
        var entry = JsonSerializer.Deserialize<TunnelStateEntry>("{\"name\":\"wg0\",\"dnsMode\":\"Split\"}", Json);

        Assert.NotNull(entry);
        Assert.Equal(NetworkManagerDnsMode.Split, entry.DnsMode);
        Assert.Contains("\"dnsMode\":\"Split\"", JsonSerializer.Serialize(entry, Json), StringComparison.Ordinal);
    }
}
