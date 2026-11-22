using VpnManager.Core.Model;
using VpnManager.Core.Presentation;
using VpnManager.Core.Services;
using VpnManager.Core.Tests.Fakes;

namespace VpnManager.Core.Tests;

/// <summary>
/// The routing mode applied on save and the journal entry a delete records.
/// Everything runs with fakes; the application no longer installs prerequisites or
/// reloads a running tunnel.
/// </summary>
public sealed class ProvisioningUiTests
{
    [Fact]
    public async Task Saving_in_full_mode_writes_the_converged_directives()
    {
        var context = Build();
        context.Presenter.BeginCreate();

        await context.Presenter.SaveAsync(Input(TunnelRoutingMode.Full, "0.0.0.0/0"));

        var write = Assert.Single(context.Store.Writes);
        Assert.Contains("Table = auto", write.Contents, StringComparison.Ordinal);
        Assert.Contains("FwMark = 51820", write.Contents, StringComparison.Ordinal);
        Assert.Contains("AllowedIPs = 0.0.0.0/0", write.Contents, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Saving_in_split_mode_with_a_default_route_is_refused_on_the_peer_field()
    {
        var context = Build();
        context.Presenter.BeginCreate();

        await context.Presenter.SaveAsync(Input(TunnelRoutingMode.Split, "0.0.0.0/0"));

        Assert.Empty(context.Store.Writes);
        Assert.Contains(
            context.View.EditorErrors,
            entry => entry.StartsWith(EditorField.PeerAllowedIps, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Saving_in_split_mode_with_a_mesh_clears_the_directives()
    {
        var context = Build();
        context.Presenter.BeginCreate();

        await context.Presenter.SaveAsync(Input(TunnelRoutingMode.Split, "10.10.0.0/24", table: "auto"));

        var write = Assert.Single(context.Store.Writes);
        Assert.DoesNotContain("Table =", write.Contents, StringComparison.Ordinal);
        Assert.Contains("AllowedIPs = 10.10.0.0/24", write.Contents, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deleting_records_a_journal_entry()
    {
        var context = Build();
        context.Store.Files["wg0"] = TestData.SampleConfigText;

        await context.Presenter.DeleteAsync("wg0");

        Assert.Contains("deleted wg0", context.Journal.Entries);
    }

    private static EditorInput Input(TunnelRoutingMode mode, string peerAllowedIps, string table = "") => new(
        "wg3",
        TestData.InterfacePrivateKey,
        "10.10.0.3/32",
        string.Empty,
        string.Empty,
        string.Empty,
        table,
        mode,
        [new EditorPeerInput("peer-one", TestData.PeerOneKey, string.Empty, peerAllowedIps, string.Empty, string.Empty)]);

    private static Context Build()
    {
        var store = new FakeWireGuardConfigStore();
        var inspector = new FakeWireGuardRuntimeInspector();
        var runner = FakeWireGuardCommandRunner.Returning(string.Empty);
        var service = new TunnelService(store, inspector);
        var view = new FakeTunnelView();
        var journal = new FakeOperationJournal();
        var presenter = new TunnelListPresenter(view, service, journal: journal);

        return new Context(presenter, view, store, journal);
    }

    private sealed record Context(
        TunnelListPresenter Presenter,
        FakeTunnelView View,
        FakeWireGuardConfigStore Store,
        FakeOperationJournal Journal);
}
