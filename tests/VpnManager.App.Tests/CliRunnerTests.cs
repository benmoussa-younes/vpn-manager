using VpnManager.App.Cli;
using VpnManager.App.Tests.Fakes;
using VpnManager.Core.Model;
using VpnManager.Core.NetworkManager;
using VpnManager.Core.Services;

namespace VpnManager.App.Tests;

public sealed class CliRunnerTests
{
    [Fact]
    public async Task StatusJsonListsTheProfileAndItsPolicy()
    {
        var (runner, _, _, _) = CreateRunner(withConnection: false);
        using var output = new StringWriter();

        var code = await runner.RunAsync(new CliOptions(CliCommand.Status, Json: true), output, TextWriter.Null);

        Assert.Equal(0, code);
        var json = output.ToString();
        Assert.Contains("\"name\": \"wg0\"", json, StringComparison.Ordinal);
        Assert.Contains("\"autoReconnect\": true", json, StringComparison.Ordinal);
        Assert.Contains("\"dnsMode\": \"Profile\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PolicyPersistsAndAppliesToTheOwnedConnection()
    {
        var (runner, _, networkManager, state) = CreateRunner(withConnection: true);
        using var output = new StringWriter();

        var code = await runner.RunAsync(
            CliOptions.Parse(["--policy", "wg0", "--auto-reconnect", "off", "--dns-mode", "split", "--dns-search", "corp.example"]),
            output,
            TextWriter.Null);

        Assert.Equal(0, code);
        Assert.False(state.State.Tunnels.Single(entry => entry.Name == "wg0").AutoReconnect);
        Assert.Equal(NetworkManagerDnsMode.Split, state.State.Tunnels.Single(entry => entry.Name == "wg0").DnsMode);
        Assert.Equal(("wg0", false, 0), Assert.Single(networkManager.AutoConnectChanges));
        var dns = Assert.Single(networkManager.DnsChanges);
        Assert.Contains("corp.example", dns.Search);
    }

    [Fact]
    public async Task ConnectAndDisconnectUseTheNetworkManagerSeam()
    {
        var (runner, _, networkManager, _) = CreateRunner(withConnection: true);

        using var connectOutput = new StringWriter();
        Assert.Equal(0, await runner.RunAsync(CliOptions.Parse(["--connect", "wg0"]), connectOutput, TextWriter.Null));
        Assert.Equal("wg0", Assert.Single(networkManager.Activated));

        using var disconnectOutput = new StringWriter();
        Assert.Equal(0, await runner.RunAsync(CliOptions.Parse(["--disconnect", "wg0"]), disconnectOutput, TextWriter.Null));
        Assert.Equal("wg0", Assert.Single(networkManager.Deactivated));
        Assert.Equal(("wg0", true, 0), networkManager.AutoConnectChanges[0]);
        Assert.Equal(("wg0", false, 0), networkManager.AutoConnectChanges[^1]);
    }

    [Fact]
    public async Task FixSysctlUsesTheRootActionSeam()
    {
        var (runner, rootActions, _, _) = CreateRunner(withConnection: false, rootAvailable: true);
        using var output = new StringWriter();

        var code = await runner.RunAsync(CliOptions.Parse(["--fix-sysctl"]), output, TextWriter.Null);

        Assert.Equal(0, code);
        Assert.Equal(1, rootActions.Calls);
        Assert.Contains("src_valid_mark=1", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FixSysctlReportsAnUnavailableHelper()
    {
        var (runner, _, _, _) = CreateRunner(withConnection: false, rootAvailable: false);
        using var output = new StringWriter();

        var code = await runner.RunAsync(CliOptions.Parse(["--fix-sysctl"]), output, TextWriter.Null);

        Assert.Equal(1, code);
        Assert.Contains("not installed", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnInvalidCommandIsATypedUsageFailure()
    {
        var (runner, _, _, _) = CreateRunner(withConnection: false);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var code = await runner.RunAsync(CliOptions.Parse(["--dns-mode", "nonsense"]), output, error);

        Assert.Equal(2, code);
        Assert.NotEqual(string.Empty, error.ToString());
    }

    private static (CliRunner Runner, FakeRootActions RootActions, FakeNetworkManager NetworkManager, FakeStateStore State) CreateRunner(
        bool withConnection,
        bool rootAvailable = false)
    {
        var store = new FakeConfigStore();
        store.Files["wg0"] = AppTestData.SampleConfig;
        var state = new FakeStateStore();
        var networkManager = new FakeNetworkManager();
        if (withConnection)
        {
            networkManager.Connections.Add(new NetworkManagerConnection("wg0", "wg0", false, ["user:alice"]));
        }

        var service = new TunnelService(
            store,
            new FakeRuntimeInspector(),
            TunnelScope.User,
            state,
            networkManager,
            "alice",
            importDirectory: Path.Combine(Path.GetTempPath(), "vpn-manager-cli-tests"));
        var rootActions = new FakeRootActions { Available = rootAvailable };
        return (new CliRunner(service, store.ConfigDirectory, rootActions), rootActions, networkManager, state);
    }
}
