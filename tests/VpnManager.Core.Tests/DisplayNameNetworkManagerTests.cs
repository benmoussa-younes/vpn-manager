using Core.Results;
using VpnManager.Core.Model;
using VpnManager.Core.NetworkManager;
using VpnManager.Core.Services;
using VpnManager.Core.Tests.Fakes;

namespace VpnManager.Core.Tests;

/// <summary>
/// The display name is the NetworkManager connection id, so it must follow the
/// rename in NetworkManager while the interface and the file keep the real name.
/// </summary>
public sealed class DisplayNameNetworkManagerTests
{
    [Fact]
    public async Task Renaming_also_renames_the_networkmanager_connection()
    {
        var (service, networkManager, _) = Build("wg0", "wg0", ["user:alice"]);

        var renamed = await service.SetDisplayName("wg0", "Home");

        Assert.True(renamed.IsSuccess);
        Assert.Equal(("wg0", "Home"), Assert.Single(networkManager.IdChanges));
    }

    [Fact]
    public async Task Clearing_the_display_name_restores_the_real_connection_id()
    {
        var (service, networkManager, _) = Build("Home", "wg0", ["user:alice"]);

        var cleared = await service.SetDisplayName("wg0", "   ");

        Assert.True(cleared.IsSuccess);
        Assert.Equal("wg0", cleared.Value);
        Assert.Equal(("Home", "wg0"), Assert.Single(networkManager.IdChanges));
    }

    [Fact]
    public async Task A_display_name_used_by_another_tunnel_is_refused()
    {
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        store.Files["wg1"] = TestData.SampleConfigText;
        var networkManager = new FakeNetworkManagerConnections();
        networkManager.Connections.Add(new NetworkManagerConnection("wg0", "wg0", false, ["user:alice"]));
        networkManager.Connections.Add(new NetworkManagerConnection("wg1", "wg1", false, ["user:alice"]));
        var service = new TunnelService(
            store,
            new FakeWireGuardRuntimeInspector(),
            TunnelScope.User,
            new FakeApplicationStateStore(),
            networkManager,
            "alice");

        var renamed = await service.SetDisplayName("wg0", "wg1");

        Assert.True(renamed.IsFailure);
        Assert.Equal(WireGuardErrorCodes.DisplayNameInvalid, renamed.Error.Code);
        Assert.Empty(networkManager.IdChanges);
    }

    [Fact]
    public async Task A_connection_the_account_does_not_own_is_left_untouched()
    {
        var (service, networkManager, _) = Build("wg0", "wg0", ["user:bob"]);

        var renamed = await service.SetDisplayName("wg0", "Home");

        Assert.True(renamed.IsSuccess);
        Assert.Empty(networkManager.IdChanges);
        var listed = await service.ListTunnelsAsync();
        Assert.True(listed.IsSuccess);
        Assert.Equal("Home", Assert.Single(listed.Value).DisplayName);
    }

    [Fact]
    public async Task A_failed_networkmanager_listing_is_not_reported_as_disconnected()
    {
        var (service, networkManager, _) = Build("wg0", "wg0", ["user:alice"], active: true);
        networkManager.ListError = new CoreError("test.list_failed", "NetworkManager is unavailable.");

        var stopped = await service.StopAsync("wg0");

        Assert.True(stopped.IsFailure);
        Assert.Empty(networkManager.Deactivated);
    }

    [Fact]
    public async Task Deleting_a_profile_keeps_a_connection_whose_id_does_not_match()
    {
        var (service, networkManager, state) = Build("Corp VPN", "wg0", ["user:alice"]);
        state.State.DisplayNames.Clear();

        var deleted = await service.DeleteAsync("wg0");

        Assert.True(deleted.IsSuccess);
        Assert.Empty(networkManager.Deleted);
    }

    [Fact]
    public async Task A_refused_networkmanager_rename_rolls_the_state_back()
    {
        var (service, networkManager, _) = Build("wg0", "wg0", ["user:alice"]);
        networkManager.IdChangeError = new CoreError("test.rename_failed", "NetworkManager refused the rename.");

        var renamed = await service.SetDisplayName("wg0", "Home");

        Assert.True(renamed.IsFailure);
        var listed = await service.ListTunnelsAsync();
        Assert.True(listed.IsSuccess);
        Assert.Null(Assert.Single(listed.Value).DisplayName);
    }

    [Fact]
    public async Task A_renamed_connection_is_listed_once()
    {
        var (service, _, state) = Build("Home", "wg0", ["user:alice"], active: true);
        state.State.DisplayNames["wg0"] = "Home";

        var listed = await service.ListTunnelsAsync();

        Assert.True(listed.IsSuccess);
        var row = Assert.Single(listed.Value);
        Assert.Equal("wg0", row.Name);
        Assert.Equal("Home", row.DisplayName);
        Assert.Equal(TunnelSource.Configuration, row.Source);
    }

    [Fact]
    public async Task Starting_after_a_rename_activates_the_connection_id()
    {
        var (service, networkManager, _) = Build("Home", "wg0", ["user:alice"]);

        var started = await service.StartAsync("wg0");

        Assert.True(started.IsSuccess);
        Assert.Equal(["Home"], networkManager.Ensured);
        Assert.Equal(["Home"], networkManager.Activated);
    }

    [Fact]
    public async Task Stopping_after_a_rename_deactivates_the_connection_id()
    {
        var (service, networkManager, _) = Build("Home", "wg0", ["user:alice"], active: true);

        var stopped = await service.StopAsync("wg0");

        Assert.True(stopped.IsSuccess);
        Assert.Equal(["Home"], networkManager.Deactivated);
        Assert.Equal(("Home", false, 0), Assert.Single(networkManager.AutoConnectChanges));
    }

    private static (TunnelService Service, FakeNetworkManagerConnections NetworkManager, FakeApplicationStateStore State) Build(
        string connectionName,
        string interfaceName,
        IReadOnlyList<string> permissions,
        bool active = false)
    {
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var state = new FakeApplicationStateStore();
        if (!string.Equals(connectionName, interfaceName, StringComparison.Ordinal))
        {
            state.State.DisplayNames["wg0"] = connectionName;
        }

        var networkManager = new FakeNetworkManagerConnections();
        networkManager.Connections.Add(new NetworkManagerConnection(connectionName, interfaceName, active, permissions));
        var service = new TunnelService(
            store,
            new FakeWireGuardRuntimeInspector(),
            TunnelScope.User,
            state,
            networkManager,
            "alice");
        return (service, networkManager, state);
    }
}
