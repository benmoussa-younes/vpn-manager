using System.Globalization;
using System.Net;

namespace VpnManager.Core.Validation;

/// <summary>
/// The small network-text rules the configuration uses. They are deliberately
/// permissive about forms the kernel accepts (<c>ip</c> takes a bare address as
/// a host prefix) and strict about anything that would make the tunnel wrong.
/// </summary>
public static class NetworkText
{
    /// <summary>
    /// Decides whether the text is an IP address, optionally with a
    /// <c>/prefix</c> that is valid for its family.
    /// </summary>
    /// <param name="value">The candidate text, for example <c>10.0.0.2/24</c> or <c>2001:db8::2</c>.</param>
    /// <returns><see langword="true"/> when the text is usable.</returns>
    public static bool IsValidAddressOrPrefix(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var slash = value.IndexOf('/', StringComparison.Ordinal);
        var addressText = slash < 0 ? value : value[..slash];
        var prefixLength = -1;

        if (slash >= 0)
        {
            var prefixText = value[(slash + 1)..];
            if (!int.TryParse(prefixText, out prefixLength))
            {
                return false;
            }
        }

        if (!IPAddress.TryParse(addressText, out var address))
        {
            return false;
        }

        var maximumPrefix = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
        return slash < 0 || (prefixLength >= 0 && prefixLength <= maximumPrefix);
    }

    /// <summary>
    /// Decides whether the text is <c>host:port</c>, including the bracketed
    /// form for an IPv6 endpoint.
    /// </summary>
    /// <param name="value">The candidate text, for example <c>vpn.example.com:51820</c>.</param>
    /// <returns><see langword="true"/> when the text is usable.</returns>
    public static bool IsValidEndpoint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string host;
        string portText;

        if (value[0] == '[')
        {
            var closing = value.IndexOf(']', StringComparison.Ordinal);
            if (closing <= 1 || closing + 1 >= value.Length || value[closing + 1] != ':')
            {
                return false;
            }

            host = value[1..closing];
            portText = value[(closing + 2)..];
        }
        else
        {
            var colon = value.LastIndexOf(':');
            if (colon <= 0 || colon == value.Length - 1)
            {
                return false;
            }

            host = value[..colon];
            portText = value[(colon + 1)..];

            // An IPv6 literal must be bracketed, otherwise the port separator and
            // the address separator are indistinguishable; wg refuses this form.
            if (host.Contains(':', StringComparison.Ordinal))
            {
                return false;
            }
        }

        if (host.Length == 0 || !IsUsableHostText(host))
        {
            return false;
        }

        return int.TryParse(portText, out var port) && port is >= 1 and <= 65535;
    }

    /// <summary>
    /// Decides whether the text is a resolver entry: an IP address, a host name, or
    /// a <c>~routing-domain</c> (a domain resolved only through the tunnel through
    /// systemd-resolved, the form the deployment uses for a split DNS).
    /// </summary>
    /// <param name="value">The candidate text.</param>
    /// <returns><see langword="true"/> when the text is usable.</returns>
    public static bool IsValidDnsEntry(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return IPAddress.TryParse(value, out _) || IsUsableHostText(value) || IsRoutingDomain(value);
    }

    /// <summary>
    /// Decides whether the text is a <c>~routing-domain</c>: a non-empty host-shaped
    /// name prefixed with <c>~</c>.
    /// </summary>
    /// <param name="value">The candidate text, for example <c>~example.com</c>.</param>
    /// <returns><see langword="true"/> when the text is a routing domain.</returns>
    public static bool IsRoutingDomain(string? value) =>
        value is { Length: > 1 } && value[0] == '~' && IsUsableHostText(value[1..]);

    /// <summary>
    /// Decides whether the text is a legal wg-quick routing-table selector:
    /// <c>auto</c>, <c>off</c>, or a route table number.
    /// </summary>
    /// <param name="value">The candidate text.</param>
    /// <returns><see langword="true"/> when the selector is usable.</returns>
    public static bool IsValidTableValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return string.Equals(value, "auto", StringComparison.Ordinal)
            || string.Equals(value, "off", StringComparison.Ordinal)
            || uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _);
    }

    /// <summary>
    /// Decides whether the text is a legal WireGuard fwmark: <c>off</c>, a 32-bit
    /// number, or a hexadecimal value with a <c>0x</c> prefix.
    /// </summary>
    /// <param name="value">The candidate text.</param>
    /// <returns><see langword="true"/> when the fwmark is usable.</returns>
    public static bool IsValidFwMark(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (string.Equals(value, "off", StringComparison.Ordinal))
        {
            return true;
        }

        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            var hexadecimal = value[2..];
            return hexadecimal.Length is > 0 and <= 8
                && uint.TryParse(hexadecimal, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);
        }

        return uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _);
    }

    private static bool IsUsableHostText(string value)
    {
        foreach (var character in value)
        {
            var allowed = char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' or ':';
            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }
}
