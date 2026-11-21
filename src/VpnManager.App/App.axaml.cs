using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Core.UI.Hosting;

namespace VpnManager.App;

/// <summary>
/// Application lifetime. The shared <see cref="CoreApplication"/> owns Fluent, the
/// theme variant and the application id; this type owns only the host, the window
/// and the single-instance activation.
/// </summary>
public partial class App : CoreApplication
{
    private ShutdownCoordinator? _shutdown;
    private PosixSignalRegistration? _sigterm;
    private PosixSignalRegistration? _sigint;

    /// <inheritdoc />
    protected override string ApplicationId => Program.ApplicationId;

    /// <inheritdoc />
    public override void Initialize()
    {
        base.Initialize();
        AvaloniaXamlLoader.Load(this);
    }

    private void OnSessionSignal(PosixSignalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var shutdown = _shutdown;
        if (shutdown is null || shutdown.IsStarted)
        {
            // No coordinator, or already shutting down: let the default
            // termination win instead of swallowing a second signal.
            return;
        }

        context.Cancel = true;
        Dispatcher.UIThread.Post(() => _ = shutdown.RunAsync().AsTask());
    }

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        ShellWindow? shell = null;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (Program.Host is not { } host)
            {
                // A headless test starts the application for its resources and
                // never creates a product host.
                base.OnFrameworkInitializationCompleted();
                return;
            }

            host.Attach(this);

            var arguments = Environment.GetCommandLineArgs();
            var configDirectory = Program.ResolveConfigDirectory(host, arguments);
            var readOnly = LaunchOptions.IsReadOnly(arguments);
            var root = new CompositionRoot(host, configDirectory, readOnly);

            shell = new ShellWindow(host, root, readOnly);

            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.MainWindow = shell.Window;

            var window = shell.Window;
            var ownedShell = shell;
            var shutdown = new ShutdownCoordinator(
                ownedShell.DisposeAsync,
                () => host.ShutdownAsync(),
                () => desktop.Shutdown());
            _shutdown = shutdown;

            // A logout or a shutdown is not an operator quit: answer the close
            // request without the disconnect dialog, and exit on SIGTERM even if
            // the window never receives a close event.
            _sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnSessionSignal);
            _sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, OnSessionSignal);
            desktop.Exit += (_, _) =>
            {
                _sigterm?.Dispose();
                _sigint?.Dispose();
            };

            window.QuitRequested += () => shutdown.RunAsync();

            // With the quit close behaviour, closing the window must end the
            // process: nothing else keeps it alive, and a window that cannot be
            // re-shown must not leave an unreachable instance behind.
            window.Closed += (_, _) => _ = shutdown.RunAsync().AsTask();

            host.ActivationRequested += () =>
            {
                if (!shutdown.IsStarted)
                {
                    Dispatcher.UIThread.Post(window.ShowWindow);
                }
            };
            host.Activated += _ =>
            {
                if (!shutdown.IsStarted)
                {
                    Dispatcher.UIThread.Post(window.ShowWindow);
                }
            };
        }

        base.OnFrameworkInitializationCompleted();

        // The base sets the startup variant, so the stored preference is applied
        // after it, or the base would win.
        shell?.ApplyAppearance();
    }
}
