using System.Diagnostics;
using Tmds.DBus.Protocol;
using VpnManager.App.DBus;
using VpnManager.App.Notifications;
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

    [Fact]
    public async Task TheServiceNoticesWhenTheSessionBusCloses()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS") is { Length: > 0 },
            "No session D-Bus address; run under dbus-run-session.");

        var dbus = new VpnManagerDbusService(
            new TunnelService(
                new FakeConfigStore(),
                new FakeRuntimeInspector(),
                TunnelScope.User,
                new FakeStateStore(),
                new FakeNetworkManager(),
                "alice"),
            "/tmp/vpn-manager-dbus-tests");
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.True(await dbus.TryStartAsync(lifetime.Token));
        Assert.False(dbus.Disconnected.IsCompleted);

        await dbus.DisposeAsync();

        var reason = await dbus.Disconnected.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(reason);
    }

    [Fact]
    public async Task TheQueryEndSessionIsAnsweredWithoutClosingTheWindow()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS") is { Length: > 0 },
            "No session D-Bus address; run under dbus-run-session.");

        using var manager = await FakeSessionManager.StartAsync();
        using var watcher = new SessionEndWatcher(FakeSessionManager.ServiceName);
        var endings = 0;
        watcher.Ending += () => Interlocked.Increment(ref endings);
        await watcher.StartAsync();

        Assert.False(watcher.IsEnding);

        await EmitAsync("QueryEndSession", "0");

        // gnome-session waits for this answer: without it a logout or a shutdown
        // hangs until the timeout.
        await Task.Delay(300);
        Assert.True(watcher.IsEnding, string.Join(",", manager.Received));
        Assert.True(await manager.Answer.WaitAsync(TimeSpan.FromSeconds(5)), string.Join(",", manager.Received));
        Assert.True(manager.AnswerAccepted);
        Assert.Equal(0, Volatile.Read(ref endings));
    }

    [Fact]
    public async Task TheEndSessionClosesTheClientOnceAndACancelKeepsIt()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS") is { Length: > 0 },
            "No session D-Bus address; run under dbus-run-session.");

        using var manager = await FakeSessionManager.StartAsync();
        using var watcher = new SessionEndWatcher(FakeSessionManager.ServiceName);
        var endings = 0;
        var closed = new TaskCompletionSource();
        watcher.Ending += () =>
        {
            Interlocked.Increment(ref endings);
            closed.TrySetResult();
        };
        await watcher.StartAsync();

        await EmitAsync("QueryEndSession", "0");
        Assert.True(await manager.Answer.WaitAsync(TimeSpan.FromSeconds(5)));
        await EmitAsync("CancelEndSession");

        var cancelled = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (watcher.IsEnding && DateTime.UtcNow < cancelled)
        {
            await Task.Delay(25);
        }

        Assert.False(watcher.IsEnding);

        await EmitAsync("QueryEndSession", "0");
        await EmitAsync("EndSession", "0");
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await EmitAsync("EndSession", "0");
        await Task.Delay(150);
        Assert.Equal(1, Volatile.Read(ref endings));
    }

    [Fact]
    public async Task TheStopSignalClosesTheClient()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS") is { Length: > 0 },
            "No session D-Bus address; run under dbus-run-session.");

        using var manager = await FakeSessionManager.StartAsync();
        using var watcher = new SessionEndWatcher(FakeSessionManager.ServiceName);
        var closed = new TaskCompletionSource();
        watcher.Ending += () => closed.TrySetResult();
        await watcher.StartAsync();

        await EmitAsync("Stop");

        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// A minimal session manager on the session bus: it owns the well-known name,
    /// answers <c>RegisterClient</c> with a client object path, records the
    /// <c>EndSessionResponse</c>, and emits the signals gnome-session would send.
    /// </summary>
    private sealed class FakeSessionManager : IPathMethodHandler, IDisposable
    {
        internal const string ServiceName = "org.example.VpnManagerSessionTest";
        private const string ManagerPath = "/org/gnome/SessionManager";
        private const string ClientPath = "/org/gnome/SessionManager/ClientTest";

        private readonly DBusConnection _connection;
        private readonly TaskCompletionSource<bool> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private FakeSessionManager(DBusConnection connection) => _connection = connection;

        internal Task<bool> Answer => _answer.Task;

        internal bool IsDisconnected { get; private set; }

        internal List<string> Received { get; } = [];

        internal bool AnswerAccepted { get; private set; }

        internal static async Task<FakeSessionManager> StartAsync()
        {
            var address = DBusAddress.Session ?? throw new InvalidOperationException("No session bus is configured.");
            var connection = new DBusConnection(address);
            await connection.ConnectAsync().ConfigureAwait(false);
            Assert.True(await connection.TryRequestNameAsync(ServiceName, RequestNameOptions.None).ConfigureAwait(false));
            var manager = new FakeSessionManager(connection);
            connection.AddMethodHandler(manager);
            _ = Task.Run(async () =>
            {
                var reason = await connection.DisconnectedAsync().ConfigureAwait(false);
                manager.IsDisconnected = true;
                Console.Error.WriteLine($"fake session manager disconnected: {reason}");
            });
            return manager;
        }

        /// <inheritdoc />
        public string Path => ManagerPath;

        /// <inheritdoc />
        public bool HandlesChildPaths => true;

        /// <inheritdoc />
        public ValueTask HandleMethodAsync(MethodContext context)
        {
            Received.Add(context.Request.MemberAsString ?? "?");
            switch (context.Request.MemberAsString)
            {
                case "RegisterClient":
                    {
                        using var writer = context.CreateReplyWriter("o");
                        writer.WriteObjectPath(ClientPath);
                        context.Reply(writer.CreateMessage());
                        return ValueTask.CompletedTask;
                    }

                case "EndSessionResponse":
                    {
                        var reader = context.Request.GetBodyReader();
                        AnswerAccepted = reader.ReadBool();
                        _ = reader.ReadString();
                        _answer.TrySetResult(true);
                        using var writer = context.CreateReplyWriter(null);
                        context.Reply(writer.CreateMessage());
                        return ValueTask.CompletedTask;
                    }

                default:
                    context.ReplyUnknownMethodError();
                    return ValueTask.CompletedTask;
            }
        }

        /// <inheritdoc />
        public void Dispose() => _connection.Dispose();
    }

    [Fact]
    public async Task TheDesktopNotifierSendsTheAppNameTheSummaryAndTheBody()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS") is { Length: > 0 },
            "No session D-Bus address; run under dbus-run-session.");

        using var daemon = await FakeNotifications.StartAsync();
        var notifier = new DesktopNotifier(FakeNotifications.ServiceName);

        await notifier.NotifyAsync("VPN connection lost", "'wg0' went down.");

        var call = await daemon.Call.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("VPN Manager", call.AppName);
        Assert.Equal("VPN connection lost", call.Summary);
        Assert.Equal("'wg0' went down.", call.Body);
    }

    /// <summary>
    /// A minimal notification daemon: it owns the name, records the summary and
    /// the body, and answers with an id.
    /// </summary>
    private sealed class FakeNotifications : IPathMethodHandler, IDisposable
    {
        internal const string ServiceName = "org.example.VpnManagerNotificationsTest";
        private const string ObjectPath = "/org/freedesktop/Notifications";

        private readonly DBusConnection _connection;
        private readonly TaskCompletionSource<(string AppName, string Summary, string Body)> _call =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private FakeNotifications(DBusConnection connection) => _connection = connection;

        internal Task<(string AppName, string Summary, string Body)> Call => _call.Task;

        internal static async Task<FakeNotifications> StartAsync()
        {
            var address = DBusAddress.Session ?? throw new InvalidOperationException("No session bus is configured.");
            var connection = new DBusConnection(address);
            await connection.ConnectAsync().ConfigureAwait(false);
            Assert.True(await connection.TryRequestNameAsync(ServiceName, RequestNameOptions.None).ConfigureAwait(false));
            var daemon = new FakeNotifications(connection);
            connection.AddMethodHandler(daemon);
            return daemon;
        }

        /// <inheritdoc />
        public string Path => ObjectPath;

        /// <inheritdoc />
        public bool HandlesChildPaths => false;

        /// <inheritdoc />
        public ValueTask HandleMethodAsync(MethodContext context)
        {
            if (context.Request.MemberAsString != "Notify")
            {
                context.ReplyUnknownMethodError();
                return ValueTask.CompletedTask;
            }

            var reader = context.Request.GetBodyReader();
            var appName = reader.ReadString();
            _ = reader.ReadUInt32();
            _ = reader.ReadString();
            var summary = reader.ReadString();
            var body = reader.ReadString();
            _call.TrySetResult((appName, summary, body));

            using var writer = context.CreateReplyWriter("u");
            writer.WriteUInt32(1);
            context.Reply(writer.CreateMessage());
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public void Dispose() => _connection.Dispose();
    }

    private static async Task EmitAsync(string member, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "gdbus",
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add("emit");
        process.StartInfo.ArgumentList.Add("--session");
        process.StartInfo.ArgumentList.Add("--object-path");
        process.StartInfo.ArgumentList.Add("/org/gnome/SessionManager/ClientTest");
        process.StartInfo.ArgumentList.Add("--signal");
        process.StartInfo.ArgumentList.Add($"org.gnome.SessionManager.ClientPrivate.{member}");
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, error);
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
