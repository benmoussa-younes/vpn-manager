using System.Diagnostics;
using VpnManager.App.DBus;
using VpnManager.App.Tests.Fakes;
using VpnManager.Core.Model;
using VpnManager.Core.NetworkManager;
using VpnManager.Core.Services;

namespace VpnManager.App.Tests;

/// <summary>
/// Real session-bus integration: run it under <c>dbus-run-session</c>. Without a
/// bus the case is skipped, because the product service is a session service.
/// </summary>
public sealed class DbusIntegrationTests
{
    [Fact]
    public async Task TheSessionServiceAnswersVersionAndStatus()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS") is { Length: > 0 },
            "No session D-Bus address; run under dbus-run-session.");

        var store = new FakeConfigStore();
        store.Files["wg0"] = AppTestData.SampleConfig;
        var networkManager = new FakeNetworkManager();
        networkManager.Connections.Add(new NetworkManagerConnection("wg0", "wg0", false, ["user:alice"]));
        var service = new TunnelService(
            store,
            new FakeRuntimeInspector(),
            TunnelScope.User,
            new FakeStateStore(),
            networkManager,
            "alice",
            importDirectory: Path.Combine(Path.GetTempPath(), "vpn-manager-dbus-tests"));

        await using var dbus = new VpnManagerDbusService(service, store.ConfigDirectory);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await dbus.StartAsync(lifetime.Token);

        var version = await CallAsync("Version");
        Assert.Contains("0.1.0", version, StringComparison.Ordinal);

        var status = await CallAsync("GetStatus");
        Assert.Contains("wg0", status, StringComparison.Ordinal);
    }

    private static async Task<string> CallAsync(string method)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "gdbus",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add("call");
        process.StartInfo.ArgumentList.Add("--session");
        process.StartInfo.ArgumentList.Add("--dest");
        process.StartInfo.ArgumentList.Add("com.bit-crafts.VpnManager");
        process.StartInfo.ArgumentList.Add("--object-path");
        process.StartInfo.ArgumentList.Add("/com/bit_crafts/VpnManager");
        process.StartInfo.ArgumentList.Add("--method");
        process.StartInfo.ArgumentList.Add(string.Concat("com.bit_crafts.VpnManager1.", method));

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(process.ExitCode == 0, error);
        return output;
    }
}
