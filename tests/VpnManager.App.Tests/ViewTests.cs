using Avalonia.Automation;
using Core.Mvp.Presentation;
using VpnManager.App.Tests.Fakes;
using VpnManager.App.Views;
using VpnManager.Core.Model;
using VpnManager.Core.Presentation;

namespace VpnManager.App.Tests;

public sealed class ViewTests
{
    [Fact]
    public async Task TheEditorNeverShowsAStoredPrivateKey()
    {
        await HeadlessTestSession.RunAsync(() =>
        {
            var view = new TunnelEditorView();

            view.Load(Snapshot(hasPrivateKey: true, publicKey: "derived-public-key"));

            Assert.Equal(string.Empty, view.PrivateKeyBox.Text);
            Assert.Contains("A key is held", view.PrivateKeyBox.PlaceholderText, StringComparison.Ordinal);
            Assert.Equal("derived-public-key", view.PublicKeyText.Text);
        });
    }

    [Fact]
    public async Task TheEditorReadsBackWhatTheOperatorTyped()
    {
        await HeadlessTestSession.RunAsync(() =>
        {
            var view = new TunnelEditorView();
            view.Load(Snapshot(hasPrivateKey: true, publicKey: "derived-public-key"));

            view.PrivateKeyBox.Text = AppTestData.InterfacePrivateKey;
            view.DnsBox.Text = "10.0.0.1";
            view.MtuBox.Text = "1420";

            var input = view.ReadInput();

            Assert.Equal(AppTestData.InterfacePrivateKey, input.PrivateKey);
            Assert.Equal("10.0.0.1", input.Dns);
            Assert.Equal("1420", input.Mtu);
            Assert.Single(input.Peers);
        });
    }

    [Fact]
    public async Task TheEditorFieldsAreLabelledForAssistiveTechnology()
    {
        await HeadlessTestSession.RunAsync(() =>
        {
            var view = new TunnelEditorView();
            view.Load(Snapshot(hasPrivateKey: false, publicKey: null));

            Assert.NotNull(AutomationProperties.GetLabeledBy(view.NameBox));
            Assert.NotNull(AutomationProperties.GetLabeledBy(view.DnsBox));

            var peerPublicKey = Assert.Single(view.PeerRows).Field(EditorField.PeerPublicKey);
            Assert.NotNull(peerPublicKey);
            Assert.NotNull(AutomationProperties.GetLabeledBy(peerPublicKey));
        });
    }

    [Fact]
    public async Task TheEditorMarksTheOffendingFieldAndClearsIt()
    {
        await HeadlessTestSession.RunAsync(() =>
        {
            var view = new TunnelEditorView();
            view.Load(Snapshot(hasPrivateKey: false, publicKey: null));

            view.SetEditorError(EditorField.Addresses, "An address is required.");

            Assert.Equal("An address is required.", view.ErrorText.Text);
            Assert.Contains("invalid", view.AddressesBox.Classes);

            view.SetEditorError(EditorField.Addresses, string.Empty);

            Assert.Equal(string.Empty, view.ErrorText.Text);
            Assert.DoesNotContain("invalid", view.AddressesBox.Classes);
        });
    }

    private static AppCommand Command(FakeApplicationHost host, string id) =>
        Assert.Single(host.Snapshot!.Commands.Commands, command => command.Id == id);

    private static TunnelListItem Item(string name, string state, TunnelSource source) =>
        new(name, state, "1 peer", "configured", null, source);

    private static TransferView Transfer() => new("0 B/s", "0 B", "0 B/s", "0 B");

    private static EditorSnapshot Snapshot(bool hasPrivateKey, string? publicKey) =>
        new(
            Title: "New tunnel",
            IsNew: false,
            Name: "wg0",
            NameEditable: false,
            Addresses: "10.0.1.1/32",
            Dns: "10.0.0.1",
            ListenPort: string.Empty,
            Mtu: string.Empty,
            Table: string.Empty,
            RoutingMode: TunnelRoutingMode.Full,
            HasPrivateKey: hasPrivateKey,
            PublicKey: publicKey,
            Peers:
            [
                new EditorPeerInput(
                    "peer-one",
                    AppTestData.PeerPublicKey,
                    string.Empty,
                    "0.0.0.0/0",
                    "vpn.example.com:51820",
                    "25"),
            ]);
}
