using VpnManager.Core.Storage;

namespace VpnManager.Core.Runtime;

/// <summary>
/// The physical default link over <c>/proc/net/route</c>. The file is world-readable,
/// so the change detector needs no privilege and starts no process.
/// </summary>
public sealed class ProcNetRoutePhysicalLinkReader : IPhysicalLinkReader
{
    private const string DefaultPath = "/proc/net/route";

    private readonly IConfigFileSystem _fileSystem;
    private readonly string _path;

    /// <summary>Initializes the reader.</summary>
    /// <param name="fileSystem">The file-system seam.</param>
    /// <param name="path">The route file; defaults to <c>/proc/net/route</c>.</param>
    public ProcNetRoutePhysicalLinkReader(IConfigFileSystem fileSystem, string path = DefaultPath)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _fileSystem = fileSystem;
        _path = path;
    }

    /// <inheritdoc />
    public PhysicalLink? Read()
    {
        var read = _fileSystem.ReadAllText(_path);
        return read.TryGetValue(out var text) && ProcNetRouteParser.TryReadDefaultRoute(text, out var link)
            ? link
            : null;
    }
}
