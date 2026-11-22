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
    public void AnEmptyDnsSearchValueMeansClearTheStoredList()
    {
        var options = CliOptions.Parse(["--policy", "wg0", "--dns-search", string.Empty]);

        Assert.Null(options.Error);
        Assert.NotNull(options.DnsSearchDomains);
        Assert.Empty(options.DnsSearchDomains);
    }

    [Fact]
    public void ADnsSearchWithoutAValueIsAnError()
    {
        var options = CliOptions.Parse(["--policy", "wg0", "--dns-search"]);

        Assert.NotNull(options.Error);
    }

    [Fact]
    public void AMissingRuntimeDirectoryIsCreatedOwnerOnlyUnderTheStateHome()
    {
        var root = Path.Combine(Path.GetTempPath(), "vpn-runtime-" + Guid.NewGuid().ToString("N"));
        var savedRuntime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        var savedState = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", null);
            Environment.SetEnvironmentVariable("XDG_STATE_HOME", root);

            var directory = Program.EnsureRuntimeDirectory();

            Assert.NotNull(directory);
            Assert.StartsWith(root, directory, StringComparison.Ordinal);
            Assert.Equal(directory, Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"));
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                File.GetUnixFileMode(directory));
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", savedRuntime);
            Environment.SetEnvironmentVariable("XDG_STATE_HOME", savedState);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void AnExistingRuntimeDirectoryIsKept()
    {
        var saved = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        try
        {
            Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", "/run/user/4242");

            Assert.Equal("/run/user/4242", Program.EnsureRuntimeDirectory());
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", saved);
        }
    }

    [Fact]
    public async Task ShutdownRunsOnceWhicheverPathAsksFirst()
    {
        var disposed = 0;
        var hostShutdown = 0;
        var desktop = 0;
        var coordinator = new ShutdownCoordinator(
            () =>
            {
                disposed++;
                return ValueTask.CompletedTask;
            },
            () =>
            {
                hostShutdown++;
                return ValueTask.CompletedTask;
            },
            () => desktop++);

        await Task.WhenAll(coordinator.RunAsync().AsTask(), coordinator.RunAsync().AsTask());
        await coordinator.RunAsync();

        Assert.True(coordinator.IsStarted);
        Assert.Equal(1, disposed);
        Assert.Equal(1, hostShutdown);
        Assert.Equal(1, desktop);
    }

    [Fact]
    public void CliParsesTheFixedPrivilegedAction()
    {
        var options = CliOptions.Parse(["--fix-sysctl"]);

        Assert.Equal(CliCommand.FixSysctl, options.Command);
        Assert.Null(options.Error);
    }

    [Fact]
    public void TheWaylandWindowIdentityMatchesTheDesktopEntry()
    {
        Assert.Equal(Program.ApplicationId, Program.WaylandOptions().AppId);
        Assert.Equal("vpn-manager", Program.WaylandOptions().AppId);
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
    public void AStateFileWithTheRetiredDnsModeStillLoads()
    {
        var directory = Path.Combine(Path.GetTempPath(), "vpn-manager-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        try
        {
            var path = Path.Combine(directory, "state.json");
            File.WriteAllText(path, """
                {
                  "version": 2,
                  "lastScope": null,
                  "tunnels": [
                    {
                      "name": "wg0",
                      "scope": "User",
                      "source": "imported",
                      "peerCount": 1,
                      "listenPort": null,
                      "updatedAt": "2026-11-22T14:22:31.9617714+00:00",
                      "verified": false,
                      "autoReconnect": true,
                      "dnsMode": "FullTunnel",
                      "dnsSearchDomains": []
                    }
                  ],
                  "displayNames": { "wg0": "Proton" }
                }
                """);
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            var store = new JsonApplicationStateStore(path, new LinuxAtomicFileWriterFactory(), new LinuxFileSystemAdapter());
            var loaded = store.Load();

            Assert.True(loaded.IsSuccess, loaded.TryGetError(out var error) ? error.Message : string.Empty);
            Assert.Equal(NetworkManagerDnsMode.Profile, loaded.Value.Tunnels[0].DnsMode);
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
