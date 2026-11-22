using VpnManager.Core.Model;
using VpnManager.Core.Services;
using VpnManager.Core.Tests.Fakes;

namespace VpnManager.Core.Tests;

/// <summary>
/// A read-only session is refused at the service, not only in the view, so no
/// caller can change a tunnel when the operator asked for a read-only run.
/// </summary>
public sealed class ReadOnlyServiceTests
{
    [Fact]
    public async Task It_refuses_to_start_a_tunnel()
    {
        var result = await ReadOnlyService().StartAsync("wg0");

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.ReadOnly, result.Error.Code);
    }

    [Fact]
    public async Task It_refuses_to_delete_a_tunnel()
    {
        var result = await ReadOnlyService().DeleteAsync("wg0");

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.ReadOnly, result.Error.Code);
    }

    [Fact]
    public void It_refuses_to_import_a_configuration()
    {
        var result = ReadOnlyService().Import("wg0", "/tmp/incoming.conf");

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.ReadOnly, result.Error.Code);
    }

    [Fact]
    public void It_refuses_to_create_and_update_a_configuration()
    {
        Assert.True(ReadOnlyService().Create(TestData.ValidConfiguration()).IsFailure);
        Assert.True(ReadOnlyService().Update(TestData.ValidConfiguration()).IsFailure);
    }

    [Fact]
    public async Task It_refuses_to_stop_a_tunnel()
    {
        var result = await ReadOnlyService().StopAsync("wg0");

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.ReadOnly, result.Error.Code);
    }

    private static TunnelService ReadOnlyService() =>
        new(
            new FakeWireGuardConfigStore(),
            new FakeWireGuardRuntimeInspector(),
            TunnelScope.User,
            new FakeApplicationStateStore(),
            new FakeNetworkManagerConnections(),
            "operator",
            readOnly: true);
}
