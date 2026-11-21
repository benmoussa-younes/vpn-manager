using System.Globalization;
using Core.Primitives;
using Core.Results;
using VpnManager.Core.Model;

namespace VpnManager.Core.Runtime;

/// <summary>
/// Parses the text output of <c>wg show</c> into model objects. It is a pure
/// function of the text, so it is tested against captured output shapes without
/// needing WireGuard, root or a display.
/// </summary>
/// <remarks>
/// A line the parser does not recognise inside a known block is ignored: the
/// text is produced by a tool this application does not control, and a newer
/// <c>wg</c> adding a line must not make the whole view fail. Structural
/// mistakes (a property before any <c>interface:</c>) are still refused.
/// </remarks>
public static class WireGuardShowParser
{
    private const string InterfacePrefix = "interface:";
    private const string PeerPrefix = "peer:";
    private const string HiddenValue = "(hidden)";
    private const string NoneValue = "(none)";

    /// <summary>
    /// Parses the whitespace-separated output of <c>wg show interfaces</c>.
    /// </summary>
    /// <param name="output">The captured standard output.</param>
    /// <returns>The interface names in the order printed.</returns>
    public static Result<IReadOnlyList<string>> ParseInterfaceNames(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return Result<IReadOnlyList<string>>.Success([]);
        }

        var names = output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Result<IReadOnlyList<string>>.Success(names);
    }

    /// <summary>
    /// Parses the output of <c>wg show</c>, which may describe several interfaces.
    /// </summary>
    /// <param name="output">The captured standard output.</param>
    /// <returns>The interfaces, or a failure naming the offending line.</returns>
    public static Result<IReadOnlyList<WireGuardInterface>> ParseInterfaces(string? output)
    {
        var interfaces = new List<WireGuardInterface>();
        if (string.IsNullOrWhiteSpace(output))
        {
            return Result<IReadOnlyList<WireGuardInterface>>.Success(interfaces);
        }

        InterfaceBuilder? current = null;
        PeerBuilder? currentPeer = null;
        var lineNumber = 0;

        foreach (var rawLine in output.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith(InterfacePrefix, StringComparison.Ordinal))
            {
                if (current is not null)
                {
                    interfaces.Add(current.Build());
                }

                current = new InterfaceBuilder(line[InterfacePrefix.Length..].Trim());
                currentPeer = null;
                continue;
            }

            if (line.StartsWith(PeerPrefix, StringComparison.Ordinal))
            {
                if (current is null)
                {
                    return Failure($"Line {lineNumber}: a peer appears before any interface.");
                }

                currentPeer = new PeerBuilder(line[PeerPrefix.Length..].Trim());
                current.Peers.Add(currentPeer);
                continue;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                return Failure($"Line {lineNumber}: '{line}' is not a 'key: value' line.");
            }

            if (current is null)
            {
                return Failure($"Line {lineNumber}: '{line}' appears before the first interface.");
            }

            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();

            if (currentPeer is null)
            {
                current.Apply(key, value);
            }
            else
            {
                currentPeer.Apply(key, value);
            }
        }

        if (current is not null)
        {
            interfaces.Add(current.Build());
        }

        return Result<IReadOnlyList<WireGuardInterface>>.Success(interfaces);
    }

    private static Result<IReadOnlyList<WireGuardInterface>> Failure(string message) =>
        Result<IReadOnlyList<WireGuardInterface>>.Failure(
            WireGuardErrors.Configuration(WireGuardErrorCodes.ParseError, message));

    private static string? NormalizeOptional(string value) =>
        value.Length == 0 || value == NoneValue ? null : value;

    private static string[] SplitList(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Reads the age out of a <c>latest handshake</c> value such as
    /// <c>1 minute, 20 seconds ago</c> or <c>Now</c>.
    /// </summary>
    /// <param name="text">The value as printed.</param>
    /// <param name="age">The parsed age.</param>
    /// <returns><see langword="true"/> when the shape was recognised.</returns>
    internal static bool TryParseHandshakeAge(string text, out TimeSpan age)
    {
        age = TimeSpan.Zero;
        var value = text.Trim();

        if (value.Equals("Now", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!value.EndsWith(" ago", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        value = value[..^" ago".Length].Trim();
        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var total = TimeSpan.Zero;

        foreach (var part in parts)
        {
            var fields = part.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (fields.Length != 2)
            {
                return false;
            }

            if (!double.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
            {
                return false;
            }

            var unit = fields[1];
            if (unit.EndsWith('s'))
            {
                unit = unit[..^1];
            }

            var secondsPerUnit = unit switch
            {
                "second" => 1d,
                "minute" => 60d,
                "hour" => 3600d,
                "day" => 86400d,
                _ => 0d,
            };

            if (secondsPerUnit == 0d)
            {
                return false;
            }

            total += TimeSpan.FromSeconds(amount * secondsPerUnit);
        }

        age = total;
        return true;
    }

    /// <summary>
    /// Reads a transfer value such as <c>1.21 KiB received, 3.50 MiB sent</c>.
    /// </summary>
    /// <param name="text">The value as printed.</param>
    /// <param name="received">The received byte count.</param>
    /// <param name="sent">The sent byte count.</param>
    /// <returns><see langword="true"/> when the shape was recognised.</returns>
    internal static bool TryParseTransfer(string text, out long received, out long sent)
    {
        received = 0;
        sent = 0;

        var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 2
            && TryParseSize(parts[0], "received", out received)
            && TryParseSize(parts[1], "sent", out sent);
    }

    private static bool TryParseSize(string text, string direction, out long bytes)
    {
        bytes = 0;

        var fields = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (fields.Length != 3 || !fields[2].Equals(direction, StringComparison.Ordinal))
        {
            return false;
        }

        if (!double.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
        {
            return false;
        }

        var multiplier = fields[1] switch
        {
            "B" => 1L,
            "KiB" => 1024L,
            "MiB" => 1024L * 1024L,
            "GiB" => 1024L * 1024L * 1024L,
            "TiB" => 1024L * 1024L * 1024L * 1024L,
            _ => 0L,
        };

        if (multiplier == 0L)
        {
            return false;
        }

        bytes = (long)Math.Round(amount * multiplier, MidpointRounding.AwayFromZero);
        return true;
    }

    /// <summary>
    /// Reads a keepalive value such as <c>every 25 seconds</c>.
    /// </summary>
    /// <param name="text">The value as printed.</param>
    /// <returns>The interval in seconds, or <see langword="null"/> when off or unrecognised.</returns>
    internal static int? ParseKeepalive(string text)
    {
        var fields = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (fields.Length == 3
            && fields[0].Equals("every", StringComparison.Ordinal)
            && fields[2].StartsWith("second", StringComparison.Ordinal)
            && int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return seconds;
        }

        return null;
    }

    private sealed class InterfaceBuilder(string name)
    {
        internal List<PeerBuilder> Peers { get; } = [];

        private string? _publicKey;
        private int? _listenPort;
        private string? _fwMark;

        internal void Apply(string key, string value)
        {
            switch (key)
            {
                case "public key":
                    _publicKey = NormalizeOptional(value);
                    break;

                case "listening port":
                    _listenPort = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var port) ? port : null;
                    break;

                case "fwmark":
                    _fwMark = value == "off" ? null : NormalizeOptional(value);
                    break;

                default:
                    // 'private key' is printed as (hidden) and is never modelled.
                    break;
            }
        }

        internal WireGuardInterface Build() => new()
        {
            Name = name,
            PublicKey = _publicKey,
            ListenPort = _listenPort,
            FwMark = _fwMark,
            Peers = Peers.Select(peer => peer.Build()).ToArray(),
        };
    }

    private sealed class PeerBuilder(string publicKey)
    {
        private bool _hasPresharedKey;
        private string? _endpoint;
        private string[] _allowedIps = [];
        private string? _handshakeText;
        private TimeSpan? _handshakeAge;
        private long _received;
        private long _sent;
        private int? _keepalive;

        internal void Apply(string key, string value)
        {
            switch (key)
            {
                case "preshared key":
                    _hasPresharedKey = value.Length != 0 && value != NoneValue;
                    break;

                case "endpoint":
                    _endpoint = NormalizeOptional(value);
                    break;

                case "allowed ips":
                    _allowedIps = SplitList(value);
                    break;

                case "latest handshake":
                    _handshakeText = NormalizeOptional(value);
                    _handshakeAge = _handshakeText is not null && TryParseHandshakeAge(_handshakeText, out var age) ? age : null;
                    break;

                case "transfer":
                    if (TryParseTransfer(value, out var received, out var sent))
                    {
                        _received = received;
                        _sent = sent;
                    }

                    break;

                case "persistent keepalive":
                    _keepalive = ParseKeepalive(value);
                    break;

                default:
                    break;
            }
        }

        internal WireGuardPeer Build() => new()
        {
            PublicKey = publicKey,
            HasPresharedKey = _hasPresharedKey,
            Endpoint = _endpoint,
            AllowedIPs = _allowedIps,
            LatestHandshakeAge = _handshakeAge,
            LatestHandshakeText = _handshakeText,
            Received = ByteSize.FromBytes(_received),
            Sent = ByteSize.FromBytes(_sent),
            PersistentKeepalive = _keepalive,
        };
    }
}
