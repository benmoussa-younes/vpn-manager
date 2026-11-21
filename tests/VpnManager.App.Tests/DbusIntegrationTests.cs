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
        Assert.True(await dbus.TryStartAsync(lifetime.Token));

        var version = await CallAsync("Version");
        Assert.Contains("0.1.0", version, StringComparison.Ordinal);

        var status = await CallAsync("GetStatus");
        Assert.Contains("wg0", status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheServiceRefusesAnUnknownDnsMode()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS") is { Length: > 0 },
            "No session D-Bus address; run under dbus-run-session.");

        var service = new TunnelService(
            new FakeConfigStore(),
            new FakeRuntimeInspector(),
            TunnelScope.User,
            new FakeStateStore(),
            new FakeNetworkManager(),
            "alice");
        await using var dbus = new VpnManagerDbusService(service, "/tmp/vpn-manager-dbus-tests");
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.True(await dbus.TryStartAsync(lifetime.Token));

        var output = await CallAsync("SetPolicy", "wg0", "true", "nonsense", "");

        Assert.Contains("Unknown DNS mode", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMalformedBodyIsRefusedAndTheServiceKeepsServing()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS") is { Length: > 0 },
            "No session D-Bus address; run under dbus-run-session.");

        var service = new TunnelService(
            new FakeConfigStore(),
            new FakeRuntimeInspector(),
            TunnelScope.User,
            new FakeStateStore(),
            new FakeNetworkManager(),
            "alice");
        await using var dbus = new VpnManagerDbusService(service, "/tmp/vpn-manager-dbus-tests");
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.True(await dbus.TryStartAsync(lifetime.Token));

        // Connect takes a string; an int32 body must be an error reply, not an
        // exception that takes the session service down.
        var malformed = await RunRawAsync("int32:42");

        Assert.NotEqual(0, malformed.ExitCode);
        Assert.Contains(VpnManagerDbusService.ErrorName, malformed.Error, StringComparison.Ordinal);

        var version = await CallAsync("Version");
        Assert.Contains("0.1.0", version, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_second_service_does_not_take_the_name()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS") is { Length: > 0 },
            "No session D-Bus address; run under dbus-run-session.");

        await using var first = new VpnManagerDbusService(
            new TunnelService(
                new FakeConfigStore(),
                new FakeRuntimeInspector(),
                TunnelScope.User,
                new FakeStateStore(),
                new FakeNetworkManager(),
                "alice"),
            "/tmp/vpn-manager-dbus-tests");
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.True(await first.TryStartAsync(lifetime.Token));

        await using var second = new VpnManagerDbusService(
            new TunnelService(
                new FakeConfigStore(),
                new FakeRuntimeInspector(),
                TunnelScope.User,
                new FakeStateStore(),
                new FakeNetworkManager(),
                "alice"),
            "/tmp/vpn-manager-dbus-tests");

        Assert.False(await second.TryStartAsync(lifetime.Token));

        var version = await CallAsync("Version");
        Assert.Contains("0.1.0", version, StringComparison.Ordinal);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunRawAsync(string typedArgument)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dbus-send",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add("--session");
        process.StartInfo.ArgumentList.Add("--print-reply");
        process.StartInfo.ArgumentList.Add("--type=method_call");
        process.StartInfo.ArgumentList.Add("--dest=com.bit-crafts.VpnManager");
        process.StartInfo.ArgumentList.Add("/com/bit_crafts/VpnManager");
        process.StartInfo.ArgumentList.Add("com.bit_crafts.VpnManager1.Connect");
        process.StartInfo.ArgumentList.Add(typedArgument);

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, output, error);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(
        string method,
        params string[] arguments)
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
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, output, error);
    }

    private static async Task<string> CallAsync(string method, params string[] arguments)
    {
        var (exitCode, output, error) = await RunAsync(method, arguments);
        Assert.True(exitCode == 0, error);
        return output;
    }
}
