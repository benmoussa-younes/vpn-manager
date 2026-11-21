using Core.Mvp.Services;
using Core.Platform.Linux;
using Core.UI.Hosting;
using VpnManager.App.Tests.Fakes;

namespace VpnManager.App.Tests;

public sealed class ShellWindowTests
{
    [Fact]
    public async Task TheShellComposesTheWindowAndTheProductContent()
    {
        var root = Path.Combine(Path.GetTempPath(), "vpn-manager-shell-tests", Guid.NewGuid().ToString("N"));
        var home = Path.Combine(root, "home");
        var configHome = Path.Combine(root, "config");
        var stateHome = Path.Combine(root, "state");
        var runtime = Path.Combine(root, "run");
        var profiles = Path.Combine(root, "profiles");
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(configHome);
        Directory.CreateDirectory(stateHome);
        Directory.CreateDirectory(runtime);
        Directory.CreateDirectory(profiles);

        var environment = new XdgEnvironment(home, configHome, stateHome, null, null, runtime, null, null);
        var created = CoreHost.Create(new CoreHostOptions(
            "benmoussa",
            "vpn-manager",
            SingleInstance: false,
            Platform: LinuxPlatform.Create(new XdgPathResolver(environment)),
            IncludeUserInSingleInstanceName: false));
        Assert.True(created.IsSuccess);
        var host = created.Value;

        try
        {
            await HeadlessTestSession.RunAsync(async () =>
            {
                var composition = new CompositionRoot(
                    host,
                    profiles,
                    processRunner: new FakeProcessRunner(),
                    stateStore: new FakeStateStore());
                await using var shell = new ShellWindow(
                    host,
                    composition,
                    readOnly: false,
                    trayHostProbe: _ => Task.FromResult(true));

                Assert.Equal("VPN Manager", shell.Window.Title);
                Assert.Equal(CloseBehavior.HideToTray, shell.Window.CloseBehavior);
                Assert.NotNull(shell.Window.Navigation.Active);
                Assert.Equal(1100, shell.Window.Width);
                Assert.Equal(720, shell.Window.Height);
            });
        }
        finally
        {
            await host.ShutdownAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ASessionEndMarksTheWatcherWithoutABus()
    {
        using var watcher = new SessionEndWatcher();

        Assert.False(watcher.IsEnding);
        watcher.MarkEnding();
        Assert.True(watcher.IsEnding);
    }

    [Fact]
    public async Task TheWindowPlacementIsRememberedAcrossLaunches()
    {
        var root = Path.Combine(Path.GetTempPath(), "vpn-manager-shell-tests", Guid.NewGuid().ToString("N"));
        var paths = new[]
        {
            Path.Combine(root, "home"),
            Path.Combine(root, "config"),
            Path.Combine(root, "state"),
            Path.Combine(root, "run"),
            Path.Combine(root, "profiles"),
        };
        foreach (var path in paths)
        {
            Directory.CreateDirectory(path);
        }

        try
        {
            await HeadlessTestSession.RunAsync(async () =>
            {
                var first = await OpenShellAsync(root, paths[4]);
                first.Window.Width = 1300;
                first.Window.Height = 800;

                var closed = new TaskCompletionSource();
                first.Window.Closed += (_, _) => closed.TrySetResult();
                first.Window.Close();
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await first.DisposeAsync();

                var second = await OpenShellAsync(root, paths[4]);
                Assert.Equal(1300, second.Window.Width);
                Assert.Equal(800, second.Window.Height);
                second.Window.Close();
                await second.DisposeAsync();
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WithoutATrayHostTheWindowQuitsInsteadOfHiding()
    {
        var root = Path.Combine(Path.GetTempPath(), "vpn-manager-shell-tests", Guid.NewGuid().ToString("N"));
        var home = Path.Combine(root, "home");
        var configHome = Path.Combine(root, "config");
        var stateHome = Path.Combine(root, "state");
        var runtime = Path.Combine(root, "run");
        var profiles = Path.Combine(root, "profiles");
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(configHome);
        Directory.CreateDirectory(stateHome);
        Directory.CreateDirectory(runtime);
        Directory.CreateDirectory(profiles);

        var environment = new XdgEnvironment(home, configHome, stateHome, null, null, runtime, null, null);
        var created = CoreHost.Create(new CoreHostOptions(
            "benmoussa",
            "vpn-manager",
            SingleInstance: false,
            Platform: LinuxPlatform.Create(new XdgPathResolver(environment)),
            IncludeUserInSingleInstanceName: false));
        Assert.True(created.IsSuccess);
        var host = created.Value;

        try
        {
            await HeadlessTestSession.RunAsync(async () =>
            {
                var composition = new CompositionRoot(
                    host,
                    profiles,
                    processRunner: new FakeProcessRunner(),
                    stateStore: new FakeStateStore());
                await using var shell = new ShellWindow(
                    host,
                    composition,
                    readOnly: false,
                    trayHostProbe: _ => Task.FromResult(false));

                Assert.Equal(CloseBehavior.Quit, shell.Window.CloseBehavior);
            });
        }
        finally
        {
            await host.ShutdownAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ASessionEndClosesWithoutAConfirmationDialog()
    {
        var root = Path.Combine(Path.GetTempPath(), "vpn-manager-shell-tests", Guid.NewGuid().ToString("N"));
        var home = Path.Combine(root, "home");
        var configHome = Path.Combine(root, "config");
        var stateHome = Path.Combine(root, "state");
        var runtime = Path.Combine(root, "run");
        var profiles = Path.Combine(root, "profiles");
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(configHome);
        Directory.CreateDirectory(stateHome);
        Directory.CreateDirectory(runtime);
        Directory.CreateDirectory(profiles);

        var environment = new XdgEnvironment(home, configHome, stateHome, null, null, runtime, null, null);
        var created = CoreHost.Create(new CoreHostOptions(
            "benmoussa",
            "vpn-manager",
            SingleInstance: false,
            Platform: LinuxPlatform.Create(new XdgPathResolver(environment)),
            IncludeUserInSingleInstanceName: false));
        Assert.True(created.IsSuccess);
        var host = created.Value;

        try
        {
            await HeadlessTestSession.RunAsync(async () =>
            {
                var composition = new CompositionRoot(
                    host,
                    profiles,
                    processRunner: new FakeProcessRunner(),
                    stateStore: new FakeStateStore());
                using var watcher = new SessionEndWatcher();
                watcher.MarkEnding();
                await using var shell = new ShellWindow(
                    host,
                    composition,
                    readOnly: false,
                    trayHostProbe: _ => Task.FromResult(false),
                    sessionEnd: watcher);

                Assert.True(await shell.Window.ConfirmClose!());
            });
        }
        finally
        {
            await host.ShutdownAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<ShellWindow> OpenShellAsync(string root, string profiles)
    {
        var environment = new XdgEnvironment(
            Path.Combine(root, "home"),
            Path.Combine(root, "config"),
            Path.Combine(root, "state"),
            null,
            null,
            Path.Combine(root, "run"),
            null,
            null);
        var created = CoreHost.Create(new CoreHostOptions(
            "benmoussa",
            "vpn-manager",
            SingleInstance: false,
            Platform: LinuxPlatform.Create(new XdgPathResolver(environment)),
            IncludeUserInSingleInstanceName: false));
        Assert.True(created.IsSuccess);
        var host = created.Value;
        var composition = new CompositionRoot(
            host,
            profiles,
            processRunner: new FakeProcessRunner(),
            stateStore: new FakeStateStore());
        var shell = new ShellWindow(host, composition, readOnly: false, trayHostProbe: _ => Task.FromResult(false));
        shell.Window.Show();
        return shell;
    }
}
