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
    /// <inheritdoc />
    protected override string ApplicationId => Program.ApplicationId;

    /// <inheritdoc />
    public override void Initialize()
    {
        base.Initialize();
        AvaloniaXamlLoader.Load(this);
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
            window.QuitRequested += async () =>
            {
                await ownedShell.DisposeAsync().ConfigureAwait(true);
                await host.ShutdownAsync().ConfigureAwait(true);
                desktop.Shutdown();
            };

            host.ActivationRequested += () => Dispatcher.UIThread.Post(window.ShowWindow);
            host.Activated += _ => Dispatcher.UIThread.Post(window.ShowWindow);
        }

        base.OnFrameworkInitializationCompleted();

        // The base sets the startup variant, so the stored preference is applied
        // after it, or the base would win.
        shell?.ApplyAppearance();
    }
}
