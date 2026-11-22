using VpnManager.Core.Configuration;
using VpnManager.Core.Model;
using VpnManager.Core.Presentation;
using VpnManager.Core.Runtime;
using VpnManager.Core.Services;
using VpnManager.Core.Tests.Fakes;

namespace VpnManager.Core.Tests;

/// <summary>
/// The defects the feature audit found and their guards. Both are regressions a
/// later change could reintroduce silently.
/// </summary>
public sealed class CoreAuditTests
{
    [Fact]
    public void A_malformed_line_does_not_echo_its_content_so_a_key_cannot_leak()
    {
        const string keyMaterial = "AKIAIOSFODNN7EXAMPLEsecretmaterial";
        var text = $"[Interface]\n{keyMaterial}\n";

        var result = WireGuardConfigParser.Parse("wg0", text);

        Assert.True(result.IsFailure);
        Assert.DoesNotContain(keyMaterial, result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_without_a_key_does_not_echo_the_value()
    {
        var text = "[Interface]\nPrivateKeyc2VjcmV0bWF0ZXJpYWw=\n";

        var result = WireGuardConfigParser.Parse("wg0", text);

        Assert.True(result.IsFailure);
        Assert.DoesNotContain("c2VjcmV0bWF0ZXJpYWw", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Selecting_a_tunnel_reads_only_that_configuration()
    {
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        store.Files["wg1"] = TestData.SampleConfigText;
        store.Files["wg2"] = TestData.SampleConfigText;

        var inspector = new FakeWireGuardRuntimeInspector();
        inspector.Interfaces["wg0"] = new WireGuardInterface { Name = "wg0", PublicKey = TestData.DerivedPublicKey };
        var service = new TunnelService(store, inspector);
        var view = new FakeTunnelView();
        var presenter = new TunnelListPresenter(view, service);

        await presenter.RefreshAsync();
        var afterRefresh = store.ReadCount;

        await presenter.SelectAsync("wg0");

        // The pane loads the selected profile once; it never re-reads the others.
        Assert.Equal(afterRefresh + 1, store.ReadCount);
        Assert.Equal("wg0", presenter.SelectedTunnel);
    }

    [Fact]
    public async Task A_successful_delete_is_reported_as_a_toast_without_a_path()
    {
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var service = new TunnelService(store, new FakeWireGuardRuntimeInspector());
        var view = new FakeTunnelView();
        var presenter = new TunnelListPresenter(view, service);

        await presenter.DeleteAsync("wg0");

        var toast = Assert.Single(view.Toasts);
        Assert.Equal("Profile deleted.", toast);
        Assert.DoesNotContain("/", toast, StringComparison.Ordinal);
    }

    [Fact]
    public void The_sysfs_counter_reader_reads_the_two_counters()
    {
        var fileSystem = new FakeConfigFileSystem();
        fileSystem.Files["/sys/class/net/wg0/statistics/rx_bytes"] = "100\n";
        fileSystem.Files["/sys/class/net/wg0/statistics/tx_bytes"] = "250\n";
        var reader = new SysfsNetworkCounterReader(fileSystem, "/sys/class/net");

        var result = reader.Read("wg0");

        Assert.True(result.IsSuccess);
        Assert.Equal(100, result.Value.ReceivedBytes);
        Assert.Equal(250, result.Value.SentBytes);
    }

    [Fact]
    public void The_sysfs_counter_reader_refuses_an_unusable_name()
    {
        var reader = new SysfsNetworkCounterReader(new FakeConfigFileSystem(), "/sys/class/net");

        var result = reader.Read("../etc/passwd");

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.TunnelNameInvalid, result.Error.Code);
    }

    [Fact]
    public void The_sysfs_counter_reader_reports_a_missing_interface()
    {
        var reader = new SysfsNetworkCounterReader(new FakeConfigFileSystem(), "/sys/class/net");

        var result = reader.Read("wg9");

        Assert.True(result.IsFailure);
    }
}
