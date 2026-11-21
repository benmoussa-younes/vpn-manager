using Core.Platform.Linux;
using Core.UI.Hosting;
using VpnManager.App.State;
using VpnManager.Core.State;

namespace VpnManager.App.Tests;

public sealed class ApplicationTests
{
    [Fact]
    public void SelfTestPassesOnTheProductContracts()
    {
        using var output = new StringWriter();

        var code = SelfTest.Run(output);

        Assert.Equal(0, code);
        Assert.Contains("SELF-TEST PASS", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void LaunchOptionsReadTheConfigDirectoryAndTheReadOnlyFlag()
    {
        string[] arguments = ["--config-dir", "/tmp/tunnels", "--read-only"];

        Assert.Equal("/tmp/tunnels", LaunchOptions.ReadValue(arguments, LaunchOptions.ConfigDirectoryFlag));
        Assert.True(LaunchOptions.IsReadOnly(arguments));
        Assert.False(LaunchOptions.IsReadOnly([]));
    }

    [Fact]
    public void StateStoreRoundTripsThroughExportAndRestore()
    {
        var directory = Path.Combine(Path.GetTempPath(), "vpn-manager-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        try
        {
            var atomicFiles = new LinuxAtomicFileWriterFactory();
            var fileSystem = new LinuxFileSystemAdapter();
            var path = Path.Combine(directory, "state.json");
            var store = new JsonApplicationStateStore(path, atomicFiles, fileSystem);

            var saved = store.Save(new ApplicationState());
            Assert.True(saved.IsSuccess, saved.TryGetError(out var saveError) ? saveError.Message : string.Empty);

            var loaded = store.Load();
            Assert.True(loaded.IsSuccess);

            var exported = Path.Combine(directory, "export.json");
            Assert.True(store.Export(exported).IsSuccess);
            Assert.True(store.LoadFrom(exported).IsSuccess);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CompositionRootUsesTheHostPathsAndTheOperatorConfigDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "vpn-manager-tests", Guid.NewGuid().ToString("N"));
        var home = Path.Combine(root, "home");
        var configHome = Path.Combine(root, "config");
        var stateHome = Path.Combine(root, "state");
        var runtime = Path.Combine(root, "run");
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(configHome);
        Directory.CreateDirectory(stateHome);
        Directory.CreateDirectory(runtime);

        var environment = new XdgEnvironment(home, configHome, stateHome, null, null, runtime, null, null);
        var platform = LinuxPlatform.Create(new XdgPathResolver(environment));
        var created = CoreHost.Create(new CoreHostOptions(
            "vpnmanager",
            "vpn-manager",
            SingleInstance: false,
            Platform: platform));

        Assert.True(created.IsSuccess);
        var host = created.Value;

        try
        {
            var profiles = Path.Combine(root, "profiles");
            Directory.CreateDirectory(profiles);

            var composition = new CompositionRoot(host, profiles);
            Assert.Equal(profiles, composition.Store.ConfigDirectory);
            Assert.Equal(host.Paths.StateFile("state.json"), Path.Combine(host.Paths.StateDirectory, "state.json"));
        }
        finally
        {
            await host.ShutdownAsync();
            Directory.Delete(root, recursive: true);
        }
    }
}
