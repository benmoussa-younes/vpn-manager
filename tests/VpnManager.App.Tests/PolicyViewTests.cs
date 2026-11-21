using VpnManager.App.Views;
using VpnManager.Core.NetworkManager;
using VpnManager.Core.Presentation;

namespace VpnManager.App.Tests;

public sealed class PolicyViewTests
{
    [Fact]
    public async Task ThePolicyPageLoadsReadsAndSubmitsEveryField()
    {
        await HeadlessTestSession.RunAsync(() =>
        {
            var view = new TunnelPolicyView();
            view.Load(new TunnelPolicySnapshot("wg0", AutoReconnect: false, NetworkManagerDnsMode.Split, ["corp.example"]));

            Assert.Equal("wg0", view.TunnelName);
            Assert.False(view.ReconnectBox.IsChecked);
            Assert.Equal((int)NetworkManagerDnsMode.Split, view.ModeBox.SelectedIndex);
            Assert.Equal("corp.example", view.SearchBox.Text);

            view.ReconnectBox.IsChecked = true;
            view.ModeBox.SelectedIndex = (int)NetworkManagerDnsMode.FullTunnel;
            view.SearchBox.Text = "a.example, b.example; c.example";

            var input = view.ReadInput();

            Assert.True(input.AutoReconnect);
            Assert.Equal(NetworkManagerDnsMode.FullTunnel, input.DnsMode);
            Assert.Equal(["a.example", "b.example", "c.example"], input.SearchDomains);
        });
    }

    [Fact]
    public async Task ThePolicyPageRaisesSaveAndCancel()
    {
        await HeadlessTestSession.RunAsync(() =>
        {
            var view = new TunnelPolicyView();
            view.Load(new TunnelPolicySnapshot("wg0", AutoReconnect: true, NetworkManagerDnsMode.Profile, []));
            TunnelPolicyInput? saved = null;
            var cancelled = false;
            view.SaveRequested += input => saved = input;
            view.CancelRequested += () => cancelled = true;

            Assert.True(view.HandleCommand("policy.save"));
            Assert.NotNull(saved);
            Assert.Equal("wg0", saved.Name);

            Assert.True(view.HandleCommand("policy.cancel"));
            Assert.True(cancelled);
            Assert.False(view.HandleCommand("policy.nothing"));
        });
    }
}
