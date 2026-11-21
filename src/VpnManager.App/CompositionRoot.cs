using Core.Diagnostics;
using Core.Platform.Linux;
using Core.UI.Hosting;
using VpnManager.Core.Model;
using VpnManager.Core.NetworkManager;
using VpnManager.Core.Runtime;
using VpnManager.Core.Services;
using VpnManager.Core.State;
using VpnManager.Core.Process;
using VpnManager.Core.Notifications;
using VpnManager.App.State;
using ProductOperationJournal = VpnManager.Core.State.IOperationJournal;
using VpnManager.Core.Storage;

namespace VpnManager.App;

/// <summary>
/// The explicit composition root of one session: it builds the real adapters over
/// the host's process, diagnostics and notification services. Every operation is
/// the operator's own; there is no elevation boundary and no machine-wide
/// directory.
/// </summary>
public sealed class CompositionRoot
{
    /// <summary>Initializes the object graph for one operator session.</summary>
    /// <param name="host">The Core host that owns the paths and the platform services.</param>
    /// <param name="configDirectory">The directory holding the profile files.</param>
    /// <param name="readOnly">Whether the session must not change anything.</param>
    /// <param name="processRunner">A process seam override, used by tests.</param>
    /// <param name="fileSystem">A file-system seam override, used by tests.</param>
    /// <param name="stateStore">The application-state store override, used by tests.</param>
    public CompositionRoot(
        CoreHost host,
        string configDirectory,
        bool readOnly = false,
        IProcessRunner? processRunner = null,
        IConfigFileSystem? fileSystem = null,
        IApplicationStateStore? stateStore = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(configDirectory);

        var atomicFiles = new LinuxAtomicFileWriterFactory();
        var platformFileSystem = new LinuxFileSystemAdapter();

        ProcessRunner = processRunner ?? new CoreProcessRunnerAdapter(host.Processes);
        FileSystem = fileSystem ?? new SystemConfigFileSystem(atomicFiles, platformFileSystem);
        StateStore = stateStore ?? new JsonApplicationStateStore(
            host.Paths.StateFile("state.json"),
            atomicFiles,
            platformFileSystem);
        Logger = host.Logger;
        Journal = new CoreOperationJournalAdapter(host.Journal);

        Store = new FileSystemWireGuardConfigStore(configDirectory, FileSystem);
        CommandRunner = new WireGuardCommandRunner(ProcessRunner);
        Runtime = new WireGuardRuntimeInspector(CommandRunner);
        CounterReader = new SysfsNetworkCounterReader(FileSystem);
        PhysicalLink = new ProcNetRoutePhysicalLinkReader(FileSystem);
        RoutingPrerequisite = new ProcRoutingPrerequisiteReader(FileSystem);
        NetworkManager = new NmcliNetworkManagerConnections(ProcessRunner);
        DnsState = new ResolvectlDnsStateReader(ProcessRunner);
        Notifier = new CoreNotifierAdapter(host.Notifier);
        Service = new TunnelService(
            Store,
            Runtime,
            TunnelScope.User,
            StateStore,
            NetworkManager,
            Environment.UserName,
            readOnly,
            DnsState,
            FileSystem,
            importDirectory: host.Paths.StateDirectory);
    }

    /// <summary>Gets the process seam.</summary>
    public IProcessRunner ProcessRunner { get; }

    /// <summary>Gets the durable application log.</summary>
    public IApplicationLogger Logger { get; }

    /// <summary>Gets the operation journal.</summary>
    public ProductOperationJournal Journal { get; }

    /// <summary>Gets the file-system seam.</summary>
    public IConfigFileSystem FileSystem { get; }

    /// <summary>Gets the WireGuard command boundary.</summary>
    public IWireGuardCommandRunner CommandRunner { get; }

    /// <summary>Gets the runtime inspector.</summary>
    public IWireGuardRuntimeInspector Runtime { get; }

    /// <summary>Gets the kernel counter reader.</summary>
    public INetworkCounterReader CounterReader { get; }

    /// <summary>Gets the physical default-link reader.</summary>
    public IPhysicalLinkReader PhysicalLink { get; }

    /// <summary>Gets the full-tunnel prerequisite reader.</summary>
    public IRoutingPrerequisiteReader RoutingPrerequisite { get; }

    /// <summary>Gets the NetworkManager boundary.</summary>
    public INetworkManagerConnections NetworkManager { get; }

    /// <summary>Gets the resolver-state reader.</summary>
    public IDnsStateReader DnsState { get; }

    /// <summary>Gets the desktop notifier.</summary>
    public INotifier Notifier { get; }

    /// <summary>Gets the application-state store.</summary>
    public IApplicationStateStore StateStore { get; }

    /// <summary>Gets the configuration store.</summary>
    public IWireGuardConfigStore Store { get; }

    /// <summary>Gets the application service the presenters use.</summary>
    public TunnelService Service { get; }
}
