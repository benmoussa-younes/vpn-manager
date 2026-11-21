using Avalonia;
using Core.Platform.Linux;
using Core.UI.Hosting;
using VpnManager.App.Cli;
using VpnManager.App.Diagnostics;
using VpnManager.App.DBus;
using VpnManager.App.Privileges;

namespace VpnManager.App;

/// <summary>
/// Entry point. The display-free paths are handled before Avalonia is configured,
/// so the published binary can run on a machine with no display.
/// </summary>
internal static class Program
{
    /// <summary>The application id: the base name of the desktop entry. It is the
    /// X11 window class and the shell's <c>ApplicationId</c>.</summary>
    internal const string ApplicationId = "vpn-manager";

    /// <summary>The vendor path component of the XDG application directories.</summary>
    internal const string Vendor = "benmoussa";

    /// <summary>The application path component of the XDG application directories.</summary>
    internal const string Application = "vpn-manager";

    /// <summary>The version reported by <c>--version</c>, read from the assembly.</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>The host of the running GUI, or <see langword="null"/> before it starts.</summary>
    internal static CoreHost? Host { get; private set; }

    private static string ReadVersion()
    {
        var version = typeof(Program).Assembly.GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    /// <summary>Runs the application, a display-free command, or the usage text.</summary>
    [STAThread]
    public static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Contains("--self-test", StringComparer.Ordinal))
        {
            return SelfTest.Run(Console.Out);
        }

        if (args.Contains("--help", StringComparer.Ordinal) || args.Contains("-h", StringComparer.Ordinal))
        {
            PrintUsage();
            return 0;
        }

        if (args.Contains("--version", StringComparer.Ordinal))
        {
            Console.WriteLine($"VPN Manager {Version}");
            Console.WriteLine("Copyright (C) 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>");
            Console.WriteLine("License GPL-3.0-or-later: GNU GPL v3 or later <https://www.gnu.org/licenses/gpl-3.0.html>.");
            Console.WriteLine("This is free software: you may change and redistribute it. There is NO WARRANTY.");
            return 0;
        }

        // Linux sets XDG_RUNTIME_DIR in a PAM session, not in every launch context.
        // Without it the single-instance socket would fall back to the sticky /tmp,
        // where another local account could pre-create the name.
        EnsureRuntimeDirectory();

        if (DiagnosticsCommand.IsRequested(args))
        {
            return RunDiagnostics(args);
        }

        if (args.Contains("--dbus", StringComparer.Ordinal))
        {
            return RunDbusAsync(args).GetAwaiter().GetResult();
        }

        if (CliOptions.IsRequested(args))
        {
            return RunCli(args);
        }

        var created = CoreHost.Create(HostOptions(singleInstance: true));
        if (created.TryGetError(out var hostError))
        {
            Console.Error.WriteLine($"vpn-manager: {hostError.Message}");
            return 1;
        }

        var host = created.Value;
        Host = host;

        try
        {
            if (!host.TryStart())
            {
                // Another instance is running and has been asked to surface.
                return 0;
            }

            StartDesktopLifetime(BuildAvaloniaApp(), args);
        }
        finally
        {
            Host = null;
            host.ShutdownAsync().AsTask().GetAwaiter().GetResult();
        }

        return 0;
    }

    /// <summary>
    /// Returns the runtime directory the host should use, creating an owner-only
    /// one under the state home when the environment has none. Linux sets
    /// <c>XDG_RUNTIME_DIR</c> in a PAM session, not in every launch context.
    /// </summary>
    /// <returns>The runtime directory, or <see langword="null"/> when neither source is usable.</returns>
    internal static string? EnsureRuntimeDirectory()
    {
        if (Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } existing)
        {
            return existing;
        }

        var stateHome = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        if (string.IsNullOrEmpty(stateHome))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home))
            {
                return null;
            }

            stateHome = Path.Combine(home, ".local", "state");
        }

        var directory = Path.Combine(stateHome, "vpn-manager", "run");
        Directory.CreateDirectory(directory);
        File.SetUnixFileMode(
            directory,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", directory);
        return directory;
    }

    /// <summary>Resolves the profile directory from the launch options.</summary>
    /// <param name="host">The host that owns the default paths.</param>
    /// <param name="arguments">The raw process arguments.</param>
    /// <returns>The absolute profile directory.</returns>
    internal static string ResolveConfigDirectory(CoreHost host, string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(arguments);

        var value = LaunchOptions.ReadValue(arguments, LaunchOptions.ConfigDirectoryFlag);
        return string.IsNullOrWhiteSpace(value) ? host.Paths.ConfigDirectory : Path.GetFullPath(value);
    }

    /// <summary>Starts the desktop lifetime; a test replaces it to check the entrypoint headlessly.</summary>
    internal static Action<AppBuilder, string[]> StartDesktopLifetime { get; set; } =
        static (builder, arguments) => builder.StartWithClassicDesktopLifetime(arguments);

    /// <summary>
    /// The Wayland window identity. It must equal the desktop entry's base name,
    /// or the GNOME shell cannot match the window to its icon and name and shows
    /// "Unknown" in the dash and the Alt+Tab switcher.
    /// </summary>
    /// <returns>The Wayland platform options.</returns>
    internal static WaylandPlatformOptions WaylandOptions() => new() { AppId = ApplicationId };

    /// <summary>Avalonia configuration; also used by the visual designer.</summary>
    /// <returns>The application builder.</returns>
    public static AppBuilder BuildAvaloniaApp() =>
        CoreAppBuilder.Configure<App>(ApplicationId)
            .With(WaylandOptions())
            .LogToTrace();

    internal static void PrintUsage()
    {
        Console.WriteLine("VPN Manager");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  vpn-manager                      start the GUI");
        Console.WriteLine("  vpn-manager --config-dir <path>  manage a specific directory");
        Console.WriteLine("  vpn-manager --read-only          browse and export, change nothing");
        Console.WriteLine("  vpn-manager --diagnostics        report what is readable without a display");
        Console.WriteLine("  vpn-manager --status [--json]    print the profiles and their policy");
        Console.WriteLine("  vpn-manager --connect <profile>  bring a profile up through NetworkManager");
        Console.WriteLine("  vpn-manager --disconnect <profile>  take a profile down");
        Console.WriteLine("  vpn-manager --policy <profile> [--auto-reconnect on|off] [--dns-mode profile|system|split|full] [--dns-search a,b|'']");
        Console.WriteLine("  vpn-manager --fix-sysctl       set the full-tunnel sysctl through polkit");
        Console.WriteLine("  vpn-manager --dbus             run the session D-Bus service");
        Console.WriteLine("  vpn-manager --self-test          run the display-free self-test");
        Console.WriteLine("  vpn-manager --version            print the version");
        Console.WriteLine("  vpn-manager --help               print this text");
    }

    private static async Task<int> RunDbusAsync(string[] arguments)
    {
        var created = CoreHost.Create(HostOptions(singleInstance: false));
        if (created.TryGetError(out var hostError))
        {
            Console.Error.WriteLine($"vpn-manager: {hostError.Message}");
            return 1;
        }

        var host = created.Value;
        using var stopping = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            stopping.Cancel();
        };
        Console.CancelKeyPress += cancel;

        VpnManagerDbusService? service = null;
        try
        {
            var root = new CompositionRoot(
                host,
                ResolveConfigDirectory(host, arguments),
                LaunchOptions.IsReadOnly(arguments));
            service = new VpnManagerDbusService(
                root.Service,
                root.Store.ConfigDirectory,
                new PkexecRootActionService(root.ProcessRunner));
            if (!await service.TryStartAsync(stopping.Token).ConfigureAwait(false))
            {
                Console.Error.WriteLine($"vpn-manager: {VpnManagerDbusService.ServiceName} is already owned by another process.");
                return 1;
            }

            Console.WriteLine($"{VpnManagerDbusService.ServiceName} on {VpnManagerDbusService.ObjectPath}");
            var idle = VpnManagerDbusService.RunAsync(stopping.Token);
            if (await Task.WhenAny(idle, service.Disconnected).ConfigureAwait(false) != idle)
            {
                // The session bus ended: exit with it instead of lingering without
                // a bus, which is what a logout would otherwise leave behind.
                Console.Error.WriteLine("vpn-manager: the session bus closed; stopping.");
                return 0;
            }

            await idle.ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"vpn-manager: D-Bus service failed: {exception.GetType().Name}");
            return 1;
        }
        finally
        {
            if (service is not null)
            {
                await service.DisposeAsync().ConfigureAwait(false);
            }

            Console.CancelKeyPress -= cancel;
            await host.ShutdownAsync().ConfigureAwait(false);
        }
    }

    private static int RunCli(string[] arguments)
    {
        var options = CliOptions.Parse(arguments);
        var created = CoreHost.Create(HostOptions(singleInstance: false));
        if (created.TryGetError(out var hostError))
        {
            Console.Error.WriteLine($"vpn-manager: {hostError.Message}");
            return 1;
        }

        var host = created.Value;
        try
        {
            var root = new CompositionRoot(
                host,
                ResolveConfigDirectory(host, arguments),
                LaunchOptions.IsReadOnly(arguments));
            var runner = new CliRunner(
                root.Service,
                root.Store.ConfigDirectory,
                new PkexecRootActionService(root.ProcessRunner));
            return runner.RunAsync(options, Console.Out, Console.Error).GetAwaiter().GetResult();
        }
        finally
        {
            host.ShutdownAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static int RunDiagnostics(string[] arguments)
    {
        var created = CoreHost.Create(HostOptions(singleInstance: false));
        if (created.TryGetError(out var hostError))
        {
            Console.Error.WriteLine($"vpn-manager: {hostError.Message}");
            return 1;
        }

        var host = created.Value;
        try
        {
            var root = new CompositionRoot(
                host,
                ResolveConfigDirectory(host, arguments),
                readOnly: true);
            return DiagnosticsCommand.RunAsync(Console.Out, root).GetAwaiter().GetResult();
        }
        finally
        {
            host.ShutdownAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static CoreHostOptions HostOptions(bool singleInstance) => new(
        Vendor,
        Application,
        SingleInstance: singleInstance,
        Platform: LinuxPlatform.Create(new XdgPathResolver()),
        IncludeUserInSingleInstanceName: false);
}
