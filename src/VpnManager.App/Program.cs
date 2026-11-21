using Avalonia;
using Core.Platform.Linux;
using Core.UI.Hosting;
using VpnManager.App.Diagnostics;

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

        if (DiagnosticsCommand.IsRequested(args))
        {
            return RunDiagnostics(args);
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

    /// <summary>Avalonia configuration; also used by the visual designer.</summary>
    /// <returns>The application builder.</returns>
    public static AppBuilder BuildAvaloniaApp() =>
        CoreAppBuilder.Configure<App>(ApplicationId).LogToTrace();

    internal static void PrintUsage()
    {
        Console.WriteLine("VPN Manager");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  vpn-manager                      start the GUI");
        Console.WriteLine("  vpn-manager --config-dir <path>  manage a specific directory");
        Console.WriteLine("  vpn-manager --read-only          browse and export, change nothing");
        Console.WriteLine("  vpn-manager --diagnostics        report what is readable without a display");
        Console.WriteLine("  vpn-manager --self-test          run the display-free self-test");
        Console.WriteLine("  vpn-manager --version            print the version");
        Console.WriteLine("  vpn-manager --help               print this text");
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
        Platform: LinuxPlatform.Create(new XdgPathResolver()));
}
