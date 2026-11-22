using VpnManager.Core.Configuration;
using VpnManager.Core.Presentation;
using VpnManager.Core.Services;
using VpnManager.Core.Tests.Fakes;

namespace VpnManager.Core.Tests;

/// <summary>
/// Importing a profile by pasting its text: the declared name is read and the
/// editor opens pre-filled, with the private key held by the presenter and never
/// rendered.
/// </summary>
public sealed class ProfileImportTests
{
    [Fact]
    public void The_declared_name_is_read_from_the_interface_comment()
    {
        Assert.Equal("wg0", WireGuardConfigParser.ReadDeclaredName(TestData.SampleConfigText));
        Assert.Null(WireGuardConfigParser.ReadDeclaredName("[Interface]\nPrivateKey = key\n"));
        Assert.Null(WireGuardConfigParser.ReadDeclaredName("[Peer]\n# Name = peer\n"));
        Assert.Null(WireGuardConfigParser.ReadDeclaredName(null));
    }

    [Fact]
    public async Task Pasting_a_profile_prefills_the_editor_with_the_declared_name()
    {
        var context = Build();

        context.View.RequestPasteProfile(TestData.SampleConfigText);
        await context.Presenter.PendingOperation!;

        Assert.True(context.View.EditorVisible);
        Assert.True(context.View.Editor!.IsNew);
        Assert.Equal("wg0", context.View.Editor.Name);
        Assert.True(context.View.Editor.HasPrivateKey);
        Assert.False(context.View.ReceivedTextContaining(TestData.InterfacePrivateKey));
    }

    [Fact]
    public async Task Pasting_malformed_text_reports_an_error_and_opens_no_editor()
    {
        var context = Build();

        context.View.RequestPasteProfile("this is not a configuration");
        await context.Presenter.PendingOperation!;

        Assert.False(context.View.EditorVisible);
        Assert.NotNull(context.View.Error);
        Assert.False(context.View.ReceivedTextContaining("this is not a configuration"));
    }

    private static Context Build()
    {
        var store = new FakeWireGuardConfigStore();
        var inspector = new FakeWireGuardRuntimeInspector();
        var runner = FakeWireGuardCommandRunner.Returning(string.Empty);
        var service = new TunnelService(store, inspector);
        var view = new FakeTunnelView();
        return new Context(new TunnelListPresenter(view, service), view);
    }

    private sealed record Context(TunnelListPresenter Presenter, FakeTunnelView View);
}
