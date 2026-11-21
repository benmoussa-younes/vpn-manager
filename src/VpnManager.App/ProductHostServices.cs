using Core.Diagnostics;
using Core.Execution;
using Core.Notifications;
using Core.Platform;
using Core.Platform.Linux;

namespace VpnManager.App;

/// <summary>
/// The Linux host services with one product decision: the single-instance claim
/// keeps the vendor and the application only, not the account name.
/// </summary>
/// <remarks>
/// WHY: Core's default name is <c>&lt;vendor&gt;-&lt;application&gt;-&lt;user&gt;</c>, but
/// the claim already lives in <c>XDG_RUNTIME_DIR</c>, which is per-user and 0700.
/// The account suffix is therefore redundant for this product. The override is
/// supplied through the public <see cref="ICoreHostServices"/> seam, so the pinned
/// Core tag stays unchanged.
/// </remarks>
internal sealed class ProductHostServices : ICoreHostServices
{
    private readonly LinuxCoreHostServices _inner = new();

    /// <inheritdoc />
    public IProcessRunner Processes => _inner.Processes;

    /// <inheritdoc />
    public ISystemLinkLauncher Links => _inner.Links;

    /// <inheritdoc />
    public IApplicationLogger CreateLogger(string path, long maxBytes, int retainedFiles) =>
        _inner.CreateLogger(path, maxBytes, retainedFiles);

    /// <inheritdoc />
    public IOperationJournal CreateJournal(string path, long maxBytes, int retainedFiles) =>
        _inner.CreateJournal(path, maxBytes, retainedFiles);

    /// <inheritdoc />
    public ICrashLog CreateCrashLog(string path, long maxBytes, int retainedFiles, int maximumLines) =>
        _inner.CreateCrashLog(path, maxBytes, retainedFiles, maximumLines);

    /// <inheritdoc />
    public ISingleInstance CreateSingleInstance(string socketPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(socketPath);

        var runtimeDirectory = Path.GetDirectoryName(socketPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeDirectory);

        // The suffix is the standard Unix-socket one; Core's .lock claim follows it.
        var productSocketPath = Path.Combine(
            runtimeDirectory,
            string.Concat(Program.Vendor, "-", Program.Application, ".socket"));

        return _inner.CreateSingleInstance(productSocketPath);
    }

    /// <inheritdoc />
    public ISystemNotifier CreateNotifier(string application) => _inner.CreateNotifier(application);

    /// <inheritdoc />
    public void PrepareStateDirectory(string path) => _inner.PrepareStateDirectory(path);
}
