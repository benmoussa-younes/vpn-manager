using VpnManager.Core.NetworkManager;

namespace VpnManager.Core.Tests;

/// <summary>The words <c>nmcli general</c> can print for connectivity.</summary>
public sealed class ConnectivityReaderTests
{
    [Theory]
    [InlineData("full\n", NetworkConnectivity.Full)]
    [InlineData("limited", NetworkConnectivity.Limited)]
    [InlineData("portal", NetworkConnectivity.Portal)]
    [InlineData("none", NetworkConnectivity.None)]
    [InlineData("", NetworkConnectivity.Unknown)]
    [InlineData("something new", NetworkConnectivity.Unknown)]
    public void The_reader_parses_every_word(string output, NetworkConnectivity expected)
    {
        Assert.Equal(expected, NmcliConnectivityReader.Parse(output));
    }
}
