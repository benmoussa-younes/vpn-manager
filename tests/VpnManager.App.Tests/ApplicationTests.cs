using Core.Platform.Linux;
using Core.UI.Hosting;
using VpnManager.App.Cli;
using VpnManager.App.Privileges;
using VpnManager.App.State;
using VpnManager.Core.NetworkManager;
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
    public void CliParsesStatusJsonAndConnect()
    {
        var status = CliOptions.Parse(["--status", "--json"]);
        Assert.Equal(CliCommand.Status, status.Command);
        Assert.True(status.Json);

        var connect = CliOptions.Parse(["--connect", "wg0"]);
        Assert.Equal(CliCommand.Connect, connect.Command);
        Assert.Equal("wg0", connect.Name);
    }

    [Fact]
    public void CliParsesThePolicyAndRejectsAnUnknownMode()
    {
        var policy = CliOptions.Parse(
        [
            "--policy", "wg0",
            "--auto-reconnect", "off",
            "--dns-mode", "split",
            "--dns-search", "corp.example,lab.example",
        ]);

        Assert.Equal(CliCommand.Policy, policy.Command);
        Assert.False(policy.AutoReconnect);
        Assert.Equal(NetworkManagerDnsMode.Split, policy.DnsMode);
        Assert.Equal(["corp.example", "lab.example"], policy.DnsSearchDomains);

        var bad = CliOptions.Parse(["--policy", "wg0", "--dns-mode", "nonsense"]);
        Assert.NotNull(bad.Error);
    }

    [Fact]
    public void CliParsesTheFixedPrivilegedAction()
    {
        var options = CliOptions.Parse(["--fix-sysctl"]);

        Assert.Equal(CliCommand.FixSysctl, options.Command);
        Assert.Null(options.Error);
    }

    [Fact]
    public void ThePrivilegedRequestCarriesOnlyTheFixedHelperAndVerb()
    {
        var request = PkexecRootActionService.BuildRequest();

        Assert.Equal(PkexecRootActionService.PkexecPath, request.FileName);
        Assert.Equal([PkexecRootActionService.HelperPath, PkexecRootActionService.Verb], request.Arguments);
    }

    [Theory]
    [InlineData("--policy", "wg0")]
    [InlineData("--policy", "wg0", "--auto-reconnect", "maybe")]
    [InlineData("--policy", "wg0", "--dns-mode", "nonsense")]
    [InlineData("--connect", "--json")]
    public void CliRejectsAnIncompleteOrInvalidPolicy(string first, params string[] rest)
    {
        var options = CliOptions.Parse([first, .. rest]);

        Assert.NotNull(options.Error);
    }

    [Fact]
    public void CliRejectsACommandWithoutAProfileName()
    {
        var options = CliOptions.Parse(["--connect"]);

        Assert.Equal(CliCommand.Connect, options.Command);
        Assert.NotNull(options.Error);
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
            "benmoussa",
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
