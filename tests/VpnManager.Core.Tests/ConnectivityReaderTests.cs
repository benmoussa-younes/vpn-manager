using VpnManager.Core.NetworkManager;

namespace VpnManager.Core.Tests;

/// <summary>The words <c>nmcli -f CONNECTIVITY,STATE general</c> can print.</summary>
public sealed class ConnectivityReaderTests
{
    [Theory]
    [InlineData("full:connected", NetworkConnectivity.Full)]
    [InlineData("limited:connected", NetworkConnectivity.Limited)]
    [InlineData("portal:connected", NetworkConnectivity.Portal)]
    [InlineData("none:disconnected", NetworkConnectivity.None)]
    [InlineData("full:asleep", NetworkConnectivity.None)]
    [InlineData("full:disconnecting", NetworkConnectivity.None)]
    [InlineData("full", NetworkConnectivity.Full)]
    [InlineData("", NetworkConnectivity.Unknown)]
    [InlineData("something new:connected", NetworkConnectivity.Unknown)]
    public void The_reader_parses_every_word(string output, NetworkConnectivity expected)
    {
        Assert.Equal(expected, NmcliConnectivityReader.Parse(output));
    }
}
