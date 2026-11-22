using VpnManager.Core.Model;
using VpnManager.Core.NetworkManager;
using VpnManager.Core.Presentation;
using VpnManager.Core.Services;
using VpnManager.Core.Tests.Fakes;

namespace VpnManager.Core.Tests;

/// <summary>The inline policy card: what it shows and what an edit stores.</summary>
public sealed class PolicyPresenterTests
{
    [Fact]
    public async Task Selecting_a_profile_shows_its_policy()
    {
        var (presenter, view, service) = Build();

        await presenter.RefreshAsync();
        await presenter.SelectAsync("wg0");

        Assert.True(view.PolicyVisible);
        Assert.NotNull(view.Policy);
        Assert.Equal("wg0", view.Policy.Name);
        Assert.True(view.Policy.AutoReconnect);
        Assert.Equal(NetworkManagerDnsMode.Profile, view.Policy.DnsMode);
        Assert.True(view.EditorVisible);
        Assert.True(service.GetPolicy("wg0").AutoReconnect);
    }

    [Fact]
    public async Task Applying_a_policy_change_stores_it()
    {
        var (presenter, view, service) = Build();

        await presenter.ApplyPolicyAsync(new TunnelPolicyInput(
            "wg0",
            AutoReconnect: false,
            NetworkManagerDnsMode.Split,
            ["corp.example"]));

        Assert.Null(view.Error);
        var stored = service.GetPolicy("wg0");
        Assert.False(stored.AutoReconnect);
        Assert.Equal(NetworkManagerDnsMode.Split, stored.DnsMode);
        Assert.Equal(["corp.example"], stored.SearchDomains);
    }

    [Fact]
    public async Task An_invalid_search_domain_is_reported()
    {
        var (presenter, view, _) = Build();

        await presenter.ApplyPolicyAsync(new TunnelPolicyInput(
            "wg0",
            AutoReconnect: true,
            NetworkManagerDnsMode.Split,
            ["1.1.1.1;touch /tmp/x"]));

        Assert.NotNull(view.Error);
    }

    private static (TunnelListPresenter Presenter, FakeTunnelView View, TunnelService Service) Build()
    {
        var store = new FakeWireGuardConfigStore();
        store.Files["wg0"] = TestData.SampleConfigText;
        var service = new TunnelService(
            store,
            new FakeWireGuardRuntimeInspector(),
            TunnelScope.User,
            new FakeApplicationStateStore());
        var view = new FakeTunnelView();
        return (new TunnelListPresenter(view, service), view, service);
    }
}
