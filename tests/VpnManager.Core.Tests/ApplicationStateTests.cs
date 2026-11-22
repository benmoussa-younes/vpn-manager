using VpnManager.Core.Model;
using VpnManager.Core.Services;
using VpnManager.Core.State;
using VpnManager.Core.Tests.Fakes;

namespace VpnManager.Core.Tests;

/// <summary>
/// The application memory: a configured but unreadable tunnel stays visible, the
/// index is updated on every operation, and a readable directory reconciles it.
/// </summary>
public sealed class ApplicationStateTests
{
    [Fact]
    public async Task A_configured_but_unreadable_tunnel_stays_in_the_list_from_the_index()
    {
        var store = new FakeWireGuardConfigStore("/etc/wireguard")
        {
            ListError = FakeWireGuardConfigStore.Denied("/etc/wireguard"),
        };
        var state = new FakeApplicationStateStore
        {
            State = new ApplicationState
            {
                Tunnels = [new TunnelStateEntry { Name = "wg0", Scope = TunnelScope.System, PeerCount = 1, ListenPort = 51820, Verified = true }],
            },
        };
        var service = new TunnelService(store, new FakeWireGuardRuntimeInspector(), TunnelScope.System, state);

        var result = await service.ListTunnelsAsync();

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value);
        Assert.Equal("wg0", row.Name);
        Assert.True(row.HasConfiguration);
        Assert.Equal(TunnelState.Down, row.State);
        Assert.Equal(1, row.PeerCount);
        Assert.Equal(51820, row.ListenPort);
    }

    [Fact]
    public async Task Without_a_state_store_an_unreadable_directory_lists_nothing_configured()
    {
        var store = new FakeWireGuardConfigStore("/etc/wireguard")
        {
            ListError = FakeWireGuardConfigStore.Denied("/etc/wireguard"),
        };
        var service = new TunnelService(store, new FakeWireGuardRuntimeInspector());

        var result = await service.ListTunnelsAsync();

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void Creating_a_tunnel_remembers_it()
    {
        var state = new FakeApplicationStateStore();
        var service = new TunnelService(new FakeWireGuardConfigStore(), new FakeWireGuardRuntimeInspector(), TunnelScope.System, state);

        Assert.True(service.Create(TestData.ValidConfiguration()).IsSuccess);

        var entry = Assert.Single(state.State.Tunnels);
        Assert.Equal("wg0", entry.Name);
        Assert.Equal(TunnelScope.System, entry.Scope);
        Assert.Equal("created", entry.Source);
        Assert.True(entry.Verified);
    }

    [Fact]
    public async Task Deleting_a_tunnel_forgets_it()
    {
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var state = new FakeApplicationStateStore
        {
            State = new ApplicationState { Tunnels = [new TunnelStateEntry { Name = "wg0", Scope = TunnelScope.System }] },
        };
        var service = new TunnelService(store, new FakeWireGuardRuntimeInspector(), TunnelScope.System, state);

        Assert.True((await service.DeleteAsync("wg0")).IsSuccess);
        Assert.Empty(state.State.Tunnels);
    }

    [Fact]
    public async Task A_passive_read_does_not_touch_the_index()
    {
        var store = new FakeWireGuardConfigStore();
        store.Files["wg1"] = TestData.SampleConfigText;
        var state = new FakeApplicationStateStore
        {
            State = new ApplicationState
            {
                Tunnels =
                [
                    new TunnelStateEntry { Name = "wg0", Scope = TunnelScope.System, Verified = true },
                    new TunnelStateEntry { Name = "wg1", Scope = TunnelScope.System, Verified = true },
                ],
            },
        };
        var service = new TunnelService(store, new FakeWireGuardRuntimeInspector(), TunnelScope.System, state);

        await service.ListTunnelsAsync();

        // A poll or --diagnostics must not rewrite the state document.
        Assert.Equal(0, state.Saves);
        Assert.Equal(2, state.State.Tunnels.Count);
    }

    [Fact]
    public async Task An_explicit_synchronize_reconciles_the_index()
    {
        var store = new FakeWireGuardConfigStore();
        store.Files["wg1"] = TestData.SampleConfigText;
        var state = new FakeApplicationStateStore
        {
            State = new ApplicationState
            {
                Tunnels =
                [
                    new TunnelStateEntry { Name = "wg0", Scope = TunnelScope.System, Verified = true },
                    new TunnelStateEntry { Name = "wg1", Scope = TunnelScope.System, Verified = true },
                ],
            },
        };
        var service = new TunnelService(store, new FakeWireGuardRuntimeInspector(), TunnelScope.System, state);

        await service.ListTunnelsAsync(reconcile: true);

        var entry = Assert.Single(state.State.Tunnels);
        Assert.Equal("wg1", entry.Name);
        Assert.True(entry.Verified);
    }

    [Fact]
    public void Importing_a_known_name_is_refused_from_the_index_without_touching_the_store()
    {
        var store = new FakeWireGuardConfigStore();
        store.External["/tmp/in.conf"] = TestData.SampleConfigText;
        var state = new FakeApplicationStateStore
        {
            State = new ApplicationState { Tunnels = [new TunnelStateEntry { Name = "wg0", Scope = TunnelScope.System, Verified = true }] },
        };
        var service = new TunnelService(store, new FakeWireGuardRuntimeInspector(), TunnelScope.System, state);

        var imported = service.Import("wg0", "/tmp/in.conf");

        Assert.True(imported.IsFailure);
        Assert.Equal(WireGuardErrorCodes.TunnelNameConflict, imported.Error.Code);
        Assert.Empty(store.Writes);
    }

    [Fact]
    public async Task Entries_from_another_scope_are_ignored()
    {
        var store = new FakeWireGuardConfigStore("/etc/wireguard")
        {
            ListError = FakeWireGuardConfigStore.Denied("/etc/wireguard"),
        };
        var state = new FakeApplicationStateStore
        {
            State = new ApplicationState
            {
                Tunnels =
                [
                    new TunnelStateEntry { Name = "sys", Scope = TunnelScope.System, Verified = true },
                    new TunnelStateEntry { Name = "mine", Scope = TunnelScope.User, Verified = true },
                ],
            },
        };
        var service = new TunnelService(store, new FakeWireGuardRuntimeInspector(), TunnelScope.System, state);

        var result = await service.ListTunnelsAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("sys", Assert.Single(result.Value).Name);
    }

    [Fact]
    public async Task Reconciling_the_same_state_twice_writes_the_index_once()
    {
        var store = new FakeWireGuardConfigStore("/etc/wireguard");
        store.Files["wg0"] = TestData.SampleConfigText;
        var state = new FakeApplicationStateStore();
        var service = new TunnelService(
            store,
            new FakeWireGuardRuntimeInspector(),
            TunnelScope.System,
            state);

        await service.ListTunnelsAsync(reconcile: true);
        var afterFirst = state.Saves;

        await service.ListTunnelsAsync(reconcile: true);

        Assert.Equal(1, afterFirst);
        Assert.Equal(afterFirst, state.Saves);
    }
}
