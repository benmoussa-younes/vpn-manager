using System.Globalization;
using VpnManager.Core.Storage;

namespace VpnManager.Core.Runtime;

/// <summary>
/// The full-tunnel prerequisite over <c>/proc/sys/net/ipv4/conf/all</c>. The files are
/// world-readable, so the check needs no privilege and starts no process.
/// </summary>
public sealed class ProcRoutingPrerequisiteReader : IRoutingPrerequisiteReader
{
    private const string SrcValidMarkPath = "/proc/sys/net/ipv4/conf/all/src_valid_mark";
    private const string RpFilterPath = "/proc/sys/net/ipv4/conf/all/rp_filter";

    private readonly IConfigFileSystem _fileSystem;

    /// <summary>Initializes the reader.</summary>
    /// <param name="fileSystem">The file-system seam.</param>
    public ProcRoutingPrerequisiteReader(IConfigFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
    }

    /// <inheritdoc />
    public RoutingPrerequisite? Read() =>
        TryReadInt(SrcValidMarkPath, out var srcValidMark) && TryReadInt(RpFilterPath, out var rpFilter)
            ? new RoutingPrerequisite(srcValidMark, rpFilter)
            : null;

    private bool TryReadInt(string path, out int value)
    {
        value = 0;
        var read = _fileSystem.ReadAllText(path);
        return read.TryGetValue(out var text)
            && int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
