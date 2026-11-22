using System.Globalization;

namespace VpnManager.Core.Runtime;

/// <summary>
/// Reads the IPv4 default route from the text of <c>/proc/net/route</c>. It is pure,
/// so the change detector is tested without touching the machine.
/// </summary>
/// <remarks>
/// The kernel prints the addresses in network byte order as an unsigned hexadecimal
/// number, so the bytes are reversed when formatted as a dotted quad. Only lines
/// whose destination is <c>00000000</c> with <c>RTF_UP</c> are considered; the one
/// with the lowest metric wins. An on-link default (gateway <c>00000000</c>) is not a
/// usable link and is ignored.
/// </remarks>
public static class ProcNetRouteParser
{
    private const uint RouteUp = 0x1;

    /// <summary>Reads the lowest-metric IPv4 default route.</summary>
    /// <param name="text">The content of <c>/proc/net/route</c>.</param>
    /// <param name="link">The link, when one was found.</param>
    /// <returns><see langword="true"/> when a usable default route was found.</returns>
    public static bool TryReadDefaultRoute(string? text, out PhysicalLink link)
    {
        link = null!;

        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var bestMetric = long.MaxValue;
        var found = false;

        foreach (var raw in text.Split('\n'))
        {
            var fields = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            // Iface Destination Gateway Flags RefCnt Use Metric Mask MTU Window IRTT
            if (fields.Length < 8
                || !string.Equals(fields[1], "00000000", StringComparison.Ordinal)
                || !uint.TryParse(fields[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var flags)
                || (flags & RouteUp) == 0
                || !long.TryParse(fields[6], NumberStyles.None, CultureInfo.InvariantCulture, out var metric)
                || !TryFormatGateway(fields[2], out var gateway)
                || metric >= bestMetric)
            {
                continue;
            }

            bestMetric = metric;
            link = new PhysicalLink(fields[0], gateway);
            found = true;
        }

        return found;
    }

    private static bool TryFormatGateway(string hexadecimal, out string gateway)
    {
        gateway = string.Empty;

        if (!uint.TryParse(hexadecimal, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) || value == 0)
        {
            return false;
        }

        gateway = string.Create(
            CultureInfo.InvariantCulture,
            $"{(value >> 0) & 0xFF}.{(value >> 8) & 0xFF}.{(value >> 16) & 0xFF}.{(value >> 24) & 0xFF}");
        return true;
    }
}
