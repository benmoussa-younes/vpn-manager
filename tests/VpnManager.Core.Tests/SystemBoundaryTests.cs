using Core.Results;
using VpnManager.Core.Model;
using VpnManager.Core.Presentation;
using VpnManager.Core.Process;
using VpnManager.Core.Services;
using VpnManager.Core.Tests.Fakes;

namespace VpnManager.Core.Tests;

/// <summary>
/// The real process boundary. These tests start a coreutils binary, never a
/// shell, and never ask for privileges.
/// </summary>
public sealed class SystemProcessRunnerTests
{
    [Fact]
    public async Task A_real_process_is_started_and_its_output_captured()
    {
        var runner = new SystemProcessRunner();

        var result = await runner.RunAsync(new ProcessRequest("/bin/echo", ["hello"]));

        Assert.True(result.Started);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("hello\n", result.StandardOutput);
        Assert.Equal(string.Empty, result.StandardError);
    }

    [Fact]
    public async Task Standard_input_is_written_and_read_back()
    {
        var runner = new SystemProcessRunner();

        var result = await runner.RunAsync(new ProcessRequest("/bin/cat", [], "round trip"));

        Assert.True(result.Started);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("round trip", result.StandardOutput);
    }

    [Fact]
    public async Task A_non_zero_exit_code_is_reported_with_its_streams()
    {
        var runner = new SystemProcessRunner();

        var result = await runner.RunAsync(new ProcessRequest("/bin/ls", ["/definitely/not/a/directory"]));

        Assert.True(result.Started);
        Assert.NotEqual(0, result.ExitCode);
        Assert.NotEqual(string.Empty, result.StandardError);
    }

    [Fact]
    public async Task An_executable_that_does_not_exist_is_reported_as_not_started()
    {
        var runner = new SystemProcessRunner();

        var result = await runner.RunAsync(new ProcessRequest("definitely-not-a-real-executable-xyz", []));

        Assert.False(result.Started);
        Assert.Equal(-1, result.ExitCode);
        Assert.NotEqual(string.Empty, result.StandardError);
    }

    [Fact]
    public async Task An_absolute_executable_runs()
    {
        var runner = new SystemProcessRunner();

        var result = await runner.RunAsync(new ProcessRequest("/bin/true", []));

        Assert.True(result.Started);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task A_null_request_is_a_programming_error()
    {
        var runner = new SystemProcessRunner();

        await Assert.ThrowsAsync<ArgumentNullException>(() => runner.RunAsync(null!));
    }
}

/// <summary>
/// The real file system refusing an operation. The modes are scripted with
/// <c>chmod</c> on a disposable directory, so the root-only behaviour is
/// reproduced without root.
/// </summary>
public sealed class SystemConfigFileSystemDenialTests
{
    [Fact]
    public void Reading_a_file_without_read_permission_is_a_permission_value()
    {
        using var directory = new TemporaryDirectory();
        var fileSystem = TestFileSystems.SystemConfig();
        var path = Path.Combine(directory.Path, "wg0.conf");
        fileSystem.WriteAllText(path, "content");

        File.SetUnixFileMode(path, UnixFileMode.None);
        try
        {
            var result = fileSystem.ReadAllText(path);

            Assert.True(result.IsFailure);
            Assert.Equal(WireGuardErrorCodes.ConfigPermissionDenied, result.Error.Code);
            Assert.Contains(path, result.Error.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public void Listing_a_directory_without_permission_is_a_permission_value()
    {
        using var directory = new TemporaryDirectory();
        var fileSystem = TestFileSystems.SystemConfig();
        var denied = Path.Combine(directory.Path, "denied");
        Directory.CreateDirectory(denied);
        File.WriteAllText(Path.Combine(denied, "wg0.conf"), "content");

        File.SetUnixFileMode(denied, UnixFileMode.None);
        try
        {
            var result = fileSystem.ListFileNames(denied);

            Assert.True(result.IsFailure);
            Assert.Equal(WireGuardErrorCodes.ConfigPermissionDenied, result.Error.Code);
        }
        finally
        {
            File.SetUnixFileMode(denied, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void Writing_into_a_directory_without_write_permission_is_a_permission_value()
    {
        using var directory = new TemporaryDirectory();
        var fileSystem = TestFileSystems.SystemConfig();
        var readOnly = Path.Combine(directory.Path, "readonly");
        Directory.CreateDirectory(readOnly);

        File.SetUnixFileMode(readOnly, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var result = fileSystem.WriteAllText(Path.Combine(readOnly, "wg0.conf"), "content");

            Assert.True(result.IsFailure);
            Assert.Equal(WireGuardErrorCodes.ConfigPermissionDenied, result.Error.Code);
        }
        finally
        {
            File.SetUnixFileMode(readOnly, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void Deleting_from_a_directory_without_write_permission_is_a_permission_value()
    {
        using var directory = new TemporaryDirectory();
        var fileSystem = TestFileSystems.SystemConfig();
        var readOnly = Path.Combine(directory.Path, "readonly");
        Directory.CreateDirectory(readOnly);
        var path = Path.Combine(readOnly, "wg0.conf");
        File.WriteAllText(path, "content");

        File.SetUnixFileMode(readOnly, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var result = fileSystem.DeleteFile(path);

            Assert.True(result.IsFailure);
            Assert.Equal(WireGuardErrorCodes.ConfigPermissionDenied, result.Error.Code);
        }
        finally
        {
            File.SetUnixFileMode(readOnly, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void Writing_over_a_symbolic_link_is_refused_rather_than_followed()
    {
        using var directory = new TemporaryDirectory();
        var fileSystem = TestFileSystems.SystemConfig();
        var target = Path.Combine(directory.Path, "real.conf");
        var link = Path.Combine(directory.Path, "wg0.conf");
        fileSystem.WriteAllText(target, "original");
        File.CreateSymbolicLink(link, target);

        var result = fileSystem.WriteAllText(link, "replacement");

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.ConfigWriteFailed, result.Error.Code);
        Assert.Equal("original", File.ReadAllText(target));
    }
}

/// <summary>The remaining service entry points, and the guards on the empty editor.</summary>
public sealed class RemainingEntryPointTests
{
    [Fact]
    public async Task Generating_a_preshared_key_is_exposed_through_the_service()
    {
        var context = CreateContext();

        var result = await context.Service.GeneratePresharedKeyAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("[redacted]", result.Value.ToString());
    }

    [Fact]
    public void Rendering_uses_the_canonical_writer()
    {
        var context = CreateContext();

        var text = TunnelService.Render(TestData.SampleConfiguration());

        Assert.Contains("[Interface]", text, StringComparison.Ordinal);
        Assert.Contains("[Peer]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Validating_is_exposed_without_touching_the_file_system()
    {
        var context = CreateContext();

        Assert.True(TunnelService.Validate(TestData.SampleConfiguration()).IsSuccess);
        Assert.True(TunnelService.Validate(TestData.SampleConfiguration() with { Name = string.Empty }).IsFailure);
    }

    [Fact]
    public async Task Saving_with_no_editor_open_does_nothing()
    {
        var context = CreateContext();

        await context.Presenter.SaveAsync(new EditorInput("wg0", string.Empty, "10.0.0.1/32", string.Empty, string.Empty, string.Empty, string.Empty, TunnelRoutingMode.Split, []));

        Assert.Empty(context.Store.Writes);
        Assert.Empty(context.View.EditorErrors);
    }

    [Fact]
    public async Task Generating_a_key_pair_with_no_editor_open_does_nothing()
    {
        var context = CreateContext();

        await context.Presenter.GenerateKeyPairAsync();

        Assert.Empty(context.View.KeyUpdates);
    }

    [Fact]
    public void Adding_or_removing_a_peer_with_no_editor_open_does_nothing()
    {
        var context = CreateContext();

        context.Presenter.AddPeer();
        context.Presenter.RemovePeer(0);

        Assert.Empty(context.View.AppendedPeers);
        Assert.Empty(context.View.RemovedPeerIndexes);
    }

    [Fact]
    public void A_failed_key_generation_is_reported_in_the_editor()
    {
        var context = CreateContext();
        context.Inspector.GenerateError = new CoreError(WireGuardErrorCodes.KeyGenerationFailed, "no wg here");
        context.Presenter.BeginCreate();

        context.View.RequestKeyPair();

        Assert.Contains(context.View.EditorErrors, entry => entry.Contains("no wg here", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_refresh_that_fails_clears_the_list_and_reports_the_reason()
    {
        var context = CreateContext();
        context.Store.ListError = new CoreError(WireGuardErrorCodes.ConfigPermissionDenied, "denied");
        context.Inspector.NamesError = new CoreError(WireGuardErrorCodes.ToolsUnavailable, "wg missing");

        await context.Presenter.RefreshAsync();

        Assert.Empty(context.View.Tunnels);
        Assert.NotNull(context.View.Error);
        Assert.False(context.View.Busy);
    }

    [Fact]
    public async Task A_removed_selection_is_forgotten_on_the_next_refresh()
    {
        var context = CreateContext();
        context.Store.Files["wg0"] = TestData.SampleConfigText;

        await context.Presenter.SelectAsync("wg0");
        context.Store.Files.Remove("wg0");
        await context.Presenter.RefreshAsync();

        Assert.Null(context.Presenter.SelectedTunnel);
        Assert.Null(context.View.SelectedName);
    }

    [Fact]
    public void Cancelling_with_no_editor_open_is_harmless()
    {
        var context = CreateContext();

        context.Presenter.CancelEditor();

        Assert.False(context.View.EditorVisible);
    }

    private static PresenterContext CreateContext()
    {
        var store = new FakeWireGuardConfigStore();
        var inspector = new FakeWireGuardRuntimeInspector();
        var runner = FakeWireGuardCommandRunner.Returning(string.Empty);
        var service = new TunnelService(store, inspector);
        var view = new FakeTunnelView();
        var presenter = new VpnManager.Core.Presentation.TunnelListPresenter(view, service);
        return new PresenterContext(presenter, view, store, inspector, service);
    }

    private sealed record PresenterContext(
        VpnManager.Core.Presentation.TunnelListPresenter Presenter,
        FakeTunnelView View,
        FakeWireGuardConfigStore Store,
        FakeWireGuardRuntimeInspector Inspector,
        TunnelService Service);
}
