using VpnManager.Core.Model;
using VpnManager.Core.NetworkManager;
using VpnManager.Core.Presentation;
using VpnManager.Core.Services;
using VpnManager.Core.Tests.Fakes;

namespace VpnManager.Core.Tests;

/// <summary>The per-profile policy editor: what it shows, saves and refuses.</summary>
public sealed class PolicyPresenterTests
{
    [Fact]
    public async Task Opening_the_policy_page_shows_the_stored_policy()
    {
        var (presenter, view, service) = Build();

        presenter.BeginPolicy("wg0");

        Assert.True(view.PolicyVisible);
        Assert.NotNull(view.Policy);
        Assert.Equal("wg0", view.Policy.Name);
        Assert.True(view.Policy.AutoReconnect);
        Assert.Equal(NetworkManagerDnsMode.Profile, view.Policy.DnsMode);
        Assert.True(service.GetPolicy("wg0").AutoReconnect);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Saving_the_policy_stores_it_and_closes_the_page()
    {
        var (presenter, view, service) = Build();

        presenter.BeginPolicy("wg0");
        await presenter.SavePolicyAsync(new TunnelPolicyInput(
            "wg0",
            AutoReconnect: false,
            NetworkManagerDnsMode.Split,
            ["corp.example"]));

        Assert.False(view.PolicyVisible);
        var stored = service.GetPolicy("wg0");
        Assert.False(stored.AutoReconnect);
        Assert.Equal(NetworkManagerDnsMode.Split, stored.DnsMode);
        Assert.Equal(["corp.example"], stored.SearchDomains);
        Assert.Contains(view.Toasts, toast => toast.Contains("Policy saved", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_invalid_search_domain_keeps_the_page_open_and_reports_the_error()
    {
        var (presenter, view, _) = Build();
        presenter.BeginPolicy("wg0");

        await presenter.SavePolicyAsync(new TunnelPolicyInput(
            "wg0",
            AutoReconnect: true,
            NetworkManagerDnsMode.Split,
            ["1.1.1.1;touch /tmp/x"]));

        Assert.True(view.PolicyVisible);
        Assert.NotNull(view.Error);
    }

    [Fact]
    public void Cancelling_the_policy_page_leaves_the_policy_untouched()
    {
        var (presenter, view, service) = Build();
        presenter.BeginPolicy("wg0");

        presenter.CancelPolicy();

        Assert.False(view.PolicyVisible);
        Assert.True(service.GetPolicy("wg0").AutoReconnect);
        Assert.Equal("Policy edit cancelled.", view.Status);
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
