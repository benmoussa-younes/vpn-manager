using VpnManager.Core.Storage;

namespace VpnManager.Core.Runtime;

/// <summary>
/// Whether the machine has a global IPv6 address, from <c>/proc/net/if_inet6</c>.
/// It is the rootless signal a partial or unusable IPv6 profile is measured against.
/// </summary>
public static class Ipv6Host
{
    private const string DefaultPath = "/proc/net/if_inet6";

    /// <summary>Decides whether the machine has a global IPv6 address.</summary>
    /// <param name="fileSystem">The file-system seam.</param>
    /// <param name="path">The address file; defaults to <c>/proc/net/if_inet6</c>.</param>
    /// <returns><see langword="true"/> when a global address exists.</returns>
    public static bool HasGlobalAddress(IConfigFileSystem fileSystem, string path = DefaultPath)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var read = fileSystem.ReadAllText(path);
        return read.TryGetValue(out var text) && TryHasGlobalAddress(text);
    }

    /// <summary>Reads the address file's text. Pure.</summary>
    /// <param name="text">The content of <c>/proc/net/if_inet6</c>.</param>
    /// <returns><see langword="true"/> when a global address exists.</returns>
    public static bool TryHasGlobalAddress(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (var raw in text.Split('\n'))
        {
            // address ifindex prefixlen scope flags devname; scope 00 is global.
            var fields = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length >= 4 && string.Equals(fields[3], "00", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
