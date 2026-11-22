using Core.Mvp.Composition;
using Core.Mvp.Presentation;
using Core.Mvp.Presenters;
using Core.Mvp.Services;
using Core.Mvp.Views;
using Core.Diagnostics;
using Core.Results;
using VpnManager.App.Privileges;
using VpnManager.Core.Model;
using VpnManager.Core.NetworkManager;
using VpnManager.Core.Process;
using VpnManager.Core.Runtime;
using VpnManager.Core.State;
using VpnManager.Core.Storage;

namespace VpnManager.App.Tests.Fakes;

internal static class AppTestData
{
    internal const string InterfacePrivateKey = "yAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBmk=";
    internal const string PeerPublicKey = "xTIBA5rboUvnH4htodjb6e697QjLERt1NAB4mZqp8Dg=";
    internal const string DerivedPublicKey = "cE6L4i5m8VQp3Z7m9n2o1r4s5t8u0w3x6y9z2a5b8c=";

    internal const string SampleConfig = """
        [Interface]
        # Name = wg0
        PrivateKey = yAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBmk=
        Address = 10.10.0.11/32
        DNS = 10.10.0.1
        [Peer]
        # Name = peer-one
        PublicKey = xTIBA5rboUvnH4htodjb6e697QjLERt1NAB4mZqp8Dg=
        AllowedIPs = 0.0.0.0/0
        Endpoint = vpn.example.com:51820
        """;
}

internal sealed class FakeConfigStore : IWireGuardConfigStore
{
    public string ConfigDirectory { get; } = "/tmp/vpn-manager-tests";

    internal Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);

    internal List<(string Name, string Content)> Writes { get; } = [];

    public string GetConfigPath(string tunnelName) => Path.Combine(ConfigDirectory, string.Concat(tunnelName, ".conf"));

    public Result<IReadOnlyList<string>> ListTunnelNames() =>
        Result<IReadOnlyList<string>>.Success(Files.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray());

    public Result<bool> Exists(string tunnelName) => Result<bool>.Success(Files.ContainsKey(tunnelName));

    public Result<string> Read(string tunnelName) =>
        Files.TryGetValue(tunnelName, out var text)
            ? Result<string>.Success(text)
            : Result<string>.Failure(new CoreError("config.not_found", $"'{tunnelName}' does not exist."));

    public Result<string> Write(string tunnelName, string configText)
    {
        Files[tunnelName] = configText;
        Writes.Add((tunnelName, configText));
        return Result<string>.Success(GetConfigPath(tunnelName));
    }

    public Result<string> Delete(string tunnelName)
    {
        Files.Remove(tunnelName);
        return Result<string>.Success(GetConfigPath(tunnelName));
    }

    public Result<string> ReadFile(string path) => Result<string>.Success(File.ReadAllText(path));

    public Result<bool> FileExists(string path) => Result<bool>.Success(File.Exists(path));

    public Result<string> WriteFile(string path, string configText)
    {
        File.WriteAllText(path, configText);
        return Result<string>.Success(path);
    }
}

internal sealed class FakeRuntimeInspector : IWireGuardRuntimeInspector
{
    internal List<string> InterfaceNames { get; } = [];

    internal Dictionary<string, WireGuardInterface> Interfaces { get; } = new(StringComparer.Ordinal);

    public Task<Result<IReadOnlyList<string>>> ListInterfaceNamesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<IReadOnlyList<string>>.Success(InterfaceNames.ToArray()));

    public Task<Result<IReadOnlyList<WireGuardInterface>>> ListInterfacesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<IReadOnlyList<WireGuardInterface>>.Success(Interfaces.Values.ToArray()));

    public Task<Result<WireGuardInterface>> GetInterfaceAsync(string name, CancellationToken cancellationToken = default) =>
        Task.FromResult(Interfaces.TryGetValue(name, out var value)
            ? Result<WireGuardInterface>.Success(value)
            : Result<WireGuardInterface>.Failure(WireGuardErrors.InterfaceNotFound(name)));

    public Task<Result<SecretKey>> GeneratePrivateKeyAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<SecretKey>.Success(SecretKey.FromValue(AppTestData.InterfacePrivateKey)));

    public Task<Result<SecretKey>> GeneratePresharedKeyAsync(CancellationToken cancellationToken = default) =>
        GeneratePrivateKeyAsync(cancellationToken);

    public Task<Result<string>> DerivePublicKeyAsync(SecretKey privateKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<string>.Success(AppTestData.DerivedPublicKey));
}

internal sealed class FakeStateStore : IApplicationStateStore
{
    internal ApplicationState State { get; set; } = new();

    internal int Saves { get; private set; }

    public Result<ApplicationState> Load() => Result<ApplicationState>.Success(State);

    public Result<string> Save(ApplicationState state)
    {
        State = state;
        Saves++;
        return Result<string>.Success("/tmp/vpn-manager-tests/state.json");
    }

    public Result<string> Export(string path) => Result<string>.Success(path);

    public Result<ApplicationState> LoadFrom(string path) => Result<ApplicationState>.Success(State);
}

internal sealed class FakeNetworkManager : INetworkManagerConnections
{
    internal List<NetworkManagerConnection> Connections { get; } = [];

    internal List<string> Ensured { get; } = [];

    internal List<string> Activated { get; } = [];

    internal List<string> Reapplied { get; } = [];

    internal List<(string Old, string New)> IdChanges { get; } = [];

    internal List<string> Deactivated { get; } = [];

    internal List<(string Name, bool AutoConnect, int Priority)> AutoConnectChanges { get; } = [];

    internal List<(string Name, IReadOnlyList<string> Dns, IReadOnlyList<string> Search, NetworkManagerDnsMode Mode, bool IncludeIpv6)> DnsChanges { get; } = [];

    public Task<Result<IReadOnlyList<NetworkManagerConnection>>> ListWireGuardConnectionsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<IReadOnlyList<NetworkManagerConnection>>.Success(Connections.ToArray()));

    public Task<Result<string>> EnsureAsync(string connectionName, string interfaceName, string configPath, string owner, CancellationToken cancellationToken = default)
    {
        Ensured.Add(connectionName);
        return Task.FromResult(Result<string>.Success(connectionName));
    }

    public Task<Result<string>> SetAutoConnectAsync(string connectionName, bool autoConnect, int priority, CancellationToken cancellationToken = default)
    {
        AutoConnectChanges.Add((connectionName, autoConnect, priority));
        return Task.FromResult(Result<string>.Success(connectionName));
    }

    public Task<Result<string>> ApplyDnsAsync(string connectionName, IReadOnlyList<string> dnsEntries, IReadOnlyList<string> searchDomains, NetworkManagerDnsMode mode, bool includeIpv6 = true, CancellationToken cancellationToken = default)
    {
        DnsChanges.Add((connectionName, dnsEntries, searchDomains, mode, includeIpv6));
        return Task.FromResult(Result<string>.Success(connectionName));
    }

    public Task<Result<string>> SetConnectionIdAsync(
        string connectionName,
        string newConnectionName,
        CancellationToken cancellationToken = default)
    {
        IdChanges.Add((connectionName, newConnectionName));
        return Task.FromResult(Result<string>.Success(newConnectionName));
    }

    public Task<Result<string>> ReapplyAsync(string deviceName, CancellationToken cancellationToken = default)
    {
        Reapplied.Add(deviceName);
        return Task.FromResult(Result<string>.Success(deviceName));
    }

    public Task<Result<string>> ActivateAsync(string connectionName, CancellationToken cancellationToken = default)
    {
        Activated.Add(connectionName);
        return Task.FromResult(Result<string>.Success(connectionName));
    }

    public Task<Result<string>> DeactivateAsync(string connectionName, CancellationToken cancellationToken = default)
    {
        Deactivated.Add(connectionName);
        return Task.FromResult(Result<string>.Success(connectionName));
    }

    public Task<Result<string>> DeleteAsync(string connectionName, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<string>.Success(connectionName));
}

internal sealed class FakeRootActions : IRootActionService
{
    internal bool Available { get; set; } = true;

    internal Result<string> Result { get; set; } = Result<string>.Success("src_valid_mark=1");

    internal int Calls { get; private set; }

    public bool IsAvailable => Available;

    public Task<Result<string>> SetSrcValidMarkAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(Result);
    }
}

internal sealed class FakeProcessRunner : IProcessRunner
{
    internal List<ProcessRequest> Requests { get; } = [];

    internal ProcessResult Result { get; set; } = ProcessResult.Completed(0, string.Empty, string.Empty);

    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return Task.FromResult(Result);
    }
}

internal sealed class FakeLogger : IApplicationLogger
{
    internal List<(LogLevel Level, string Message)> Entries { get; } = [];

    public void Log(LogLevel level, string message) => Entries.Add((level, message));

    public void Dispose()
    {
    }
}

internal sealed class FakeNotifications : INotificationService
{
    internal List<(string Message, MessageSeverity Severity)> Toasts { get; } = [];

    internal List<(string Message, MessageSeverity Severity)> Banners { get; } = [];

    public void Toast(string message, MessageSeverity severity = MessageSeverity.Information) => Toasts.Add((message, severity));

    public void Banner(string message, MessageSeverity severity = MessageSeverity.Error) => Banners.Add((message, severity));

    public void DismissBanner() => Banners.Clear();
}

internal sealed class FakeDialogs : IDialogService
{
    internal ConfirmResult Confirm { get; set; } = ConfirmResult.Accept;

    internal List<ConfirmRequest> Requests { get; } = [];

    public ValueTask<ConfirmResult> ConfirmAsync(ConfirmRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return ValueTask.FromResult(Confirm);
    }

    internal int SavePrompts { get; private set; }

    internal SavePromptChoice SavePromptAnswer { get; set; } = SavePromptChoice.Discard;

    public ValueTask<SavePromptChoice> ConfirmSaveAsync(string documentName, CancellationToken cancellationToken = default)
    {
        SavePrompts++;
        return ValueTask.FromResult(SavePromptAnswer);
    }

    public ValueTask<QuitChoice> ConfirmQuitAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(QuitChoice.Quit);
}

internal sealed class FakePrompts : IPromptService
{
    public ValueTask<string?> PromptAsync(PromptRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<string?>(request.Value);
}

internal sealed class FakeFiles : IFilePicker, IFolderPicker
{
    internal List<string> Open { get; set; } = [];

    internal string? Save { get; set; }

    public ValueTask<IReadOnlyList<string>> OpenFilesAsync(FilePickerOptions options, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<string>>(Open);

    public ValueTask<string?> SaveFileAsync(SaveFilePickerOptions options, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Save);

    public ValueTask<string?> PickFolderAsync(FolderPickerOptions options, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<string?>(null);
}

internal sealed class FakeClipboard : IClipboardService
{
    internal string? Text { get; set; }

    public ValueTask SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        Text = text;
        return ValueTask.CompletedTask;
    }

    public ValueTask<string?> GetTextAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Text);

    public ValueTask SetImageAsync(byte[] png, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask<byte[]?> GetImageAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<byte[]?>(null);
}

internal sealed class FakeNavigation : INavigationHost
{
    internal List<(string Title, string Subtitle, IView View)> Pushed { get; } = [];

    internal int Pops { get; private set; }

    public IView? Active => Pushed.Count > 0 ? Pushed[^1].View : null;

    public void OpenTab(string id, string title, IView view) => Pushed.Add((title, string.Empty, view));

    public void CloseTab(string id) { }

    public void Push(string title, string subtitle, IView view) => Pushed.Add((title, subtitle, view));

    public void Pop()
    {
        Pops++;
        if (Pushed.Count > 0)
        {
            Pushed.RemoveAt(Pushed.Count - 1);
        }
    }
}

internal sealed class FakeTray : ITrayService
{
    // The product has no tray; the host contract still requires the member.
    public void Show(string toolTip, CommandSet menu)
    {
    }

    public void Hide()
    {
    }
}

internal sealed class FakeApplicationHost : IApplicationHost
{
    internal FakeNotifications NotificationsService { get; } = new();

    internal FakeDialogs DialogsService { get; } = new();

    internal FakeNavigation NavigationService { get; } = new();

    internal FakeTray TrayService { get; } = new();

    internal FakeFiles FilesService { get; } = new();

    internal FakeClipboard ClipboardService { get; } = new();

    internal FakePrompts PromptsService { get; } = new();

    internal ShellSnapshot? Snapshot { get; private set; }

    internal int ShowWindowCount { get; private set; }

    internal int HideWindowCount { get; private set; }

    internal int QuitCount { get; private set; }

    public IDialogService Dialogs => DialogsService;

    public IPromptService Prompts => PromptsService;

    public INotificationService Notifications => NotificationsService;

    public IFilePicker Files => FilesService;

    public IFolderPicker Folders => FilesService;

    public IClipboardService Clipboard => ClipboardService;

    public INavigationHost Navigation => NavigationService;

    public ITrayService Tray => TrayService;

    public CloseBehavior CloseBehavior { get; set; } = CloseBehavior.Quit;

    public event Action<IView, string>? CommandInvoked;

    public event Action? Activated;

    public void SetSnapshot(ShellSnapshot snapshot) => Snapshot = snapshot;

    public void SetSnapshot(IViewPresenter presenter, ShellSnapshot snapshot) => Snapshot = snapshot;

    public void SetApplicationMenu(CommandSet menu) { }

    public void ShowWindow() => ShowWindowCount++;

    public void HideWindow() => HideWindowCount++;

    public void Quit() => QuitCount++;

    internal void RaiseCommand(IView view, string id) => CommandInvoked?.Invoke(view, id);

    internal void RaiseActivated() => Activated?.Invoke();
}
