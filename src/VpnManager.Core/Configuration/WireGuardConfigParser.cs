using System.Globalization;
using Core.Results;
using VpnManager.Core.Model;

namespace VpnManager.Core.Configuration;

/// <summary>
/// Reads the wg-quick configuration text format into a
/// <see cref="TunnelConfiguration"/>.
/// </summary>
/// <remarks>
/// The format is the one <c>wg-quick</c> documents: an <c>[Interface]</c>
/// section followed by zero or more <c>[Peer]</c> sections, <c>Key = Value</c>
/// lines, <c>#</c> comments, and repeated <c>Address</c>, <c>DNS</c>,
/// <c>AllowedIPs</c> and hook lines. A <c># Name = …</c> comment that directly
/// precedes a <c>[Peer]</c> header becomes that peer's label. A key the format
/// does not define is refused rather than silently dropped.
/// </remarks>
public static class WireGuardConfigParser
{
    private const string InterfaceSection = "[Interface]";
    private const string PeerSection = "[Peer]";

    /// <summary>The largest configuration text the parser accepts.</summary>
    public const int MaximumTextLength = 1_048_576;

    /// <summary>
    /// Parses configuration text into a configuration named <paramref name="name"/>.
    /// </summary>
    /// <param name="name">The tunnel name, normally the configuration file stem.</param>
    /// <param name="text">The configuration text.</param>
    /// <returns>The parsed configuration, or a failure naming the offending line.</returns>
    public static Result<TunnelConfiguration> Parse(string name, string? text)
    {
        if (text is null)
        {
            return Failure("The configuration text is empty.");
        }

        if (text.Length > MaximumTextLength)
        {
            return Failure($"The configuration text is larger than {MaximumTextLength} characters; refusing to parse it.");
        }

        var lines = Preprocess(text);
        var interfaceBuilder = InterfaceBuilder.Empty;
        var peerBuilders = new List<PeerBuilder>();
        PeerBuilder? currentPeer = null;
        var interfaceSeen = false;

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var lineNumber = index + 1;

            if (line.Content.Length == 0)
            {
                // A '# Name = x' comment directly after a [Peer] header, before
                // any key, labels that peer. The same comment directly before the
                // header is handled when the header is read.
                if (currentPeer is not null
                    && currentPeer.Label is null
                    && !currentPeer.HasAnyKey
                    && TryReadNameLabel(line.Comment, out var inlineLabel))
                {
                    currentPeer.Label = inlineLabel;
                }

                continue;
            }

            if (line.Content == InterfaceSection)
            {
                if (interfaceSeen)
                {
                    return Failure($"Line {lineNumber}: a second [Interface] section is not allowed; a configuration has exactly one.");
                }

                interfaceSeen = true;
                currentPeer = null;
                continue;
            }

            if (line.Content == PeerSection)
            {
                currentPeer = new PeerBuilder { Label = NameLabelBefore(lines, index) };
                peerBuilders.Add(currentPeer);
                continue;
            }

            var separator = line.Content.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                // The line content is deliberately not echoed: a malformed line may
                // be a bare private key, and an error message may be shown or logged.
                return Failure($"Line {lineNumber}: expected 'Key = Value'.");
            }

            var key = line.Content[..separator].Trim();
            var value = line.Content[(separator + 1)..].Trim();

            if (currentPeer is null)
            {
                var interfaceFailure = interfaceBuilder.Apply(key, value, lineNumber);
                if (interfaceFailure is not null)
                {
                    return Result<TunnelConfiguration>.Failure(interfaceFailure);
                }
            }
            else
            {
                var peerFailure = currentPeer.Apply(key, value, lineNumber);
                if (peerFailure is not null)
                {
                    return Result<TunnelConfiguration>.Failure(peerFailure);
                }
            }
        }

        if (!interfaceSeen)
        {
            return Failure("The configuration has no [Interface] section.");
        }

        var configuration = new TunnelConfiguration
        {
            Name = name,
            Interface = interfaceBuilder.Build(),
            Peers = peerBuilders.Select(peer => peer.Build()).ToArray(),
        };

        return Result<TunnelConfiguration>.Success(configuration);
    }

    private static Result<TunnelConfiguration> Failure(string message) =>
        Result<TunnelConfiguration>.Failure(WireGuardErrors.Configuration(WireGuardErrorCodes.ConfigParseError, message));

    /// <summary>
    /// Reads the interface name declared by a <c># Name = …</c> comment placed
    /// directly under <c>[Interface]</c>, which is how this application writes it.
    /// </summary>
    /// <param name="text">The configuration text.</param>
    /// <returns>The declared name, or <see langword="null"/> when there is none.</returns>
    public static string? ReadDeclaredName(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var lines = Preprocess(text);
        for (var index = 0; index < lines.Count; index++)
        {
            if (lines[index].Content != InterfaceSection)
            {
                continue;
            }

            for (var next = index + 1; next < lines.Count && lines[next].Content.Length == 0; next++)
            {
                if (TryReadNameLabel(lines[next].Comment, out var label))
                {
                    return label;
                }
            }

            return null;
        }

        return null;
    }

    private static List<SourceLine> Preprocess(string text)
    {
        var rawLines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var lines = new List<SourceLine>(rawLines.Length);

        foreach (var raw in rawLines)
        {
            var hash = raw.IndexOf('#', StringComparison.Ordinal);
            var content = (hash < 0 ? raw : raw[..hash]).Trim();
            var comment = hash < 0 ? string.Empty : raw[(hash + 1)..].Trim();

            if (content.Equals(InterfaceSection, StringComparison.OrdinalIgnoreCase))
            {
                content = InterfaceSection;
            }
            else if (content.Equals(PeerSection, StringComparison.OrdinalIgnoreCase))
            {
                content = PeerSection;
            }

            lines.Add(new SourceLine(content, comment));
        }

        return lines;
    }

    private static string? NameLabelBefore(List<SourceLine> lines, int peerIndex)
    {
        for (var index = peerIndex - 1; index >= 0; index--)
        {
            var line = lines[index];
            if (line.Content.Length != 0)
            {
                return null;
            }

            if (TryReadNameLabel(line.Comment, out var label))
            {
                return label;
            }
        }

        return null;
    }

    private static bool TryReadNameLabel(string comment, out string label)
    {
        label = string.Empty;

        if (!comment.StartsWith("Name", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var separator = comment.IndexOf('=', StringComparison.Ordinal);
        if (separator <= 0)
        {
            return false;
        }

        var value = comment[(separator + 1)..].Trim();
        if (value.Length == 0)
        {
            return false;
        }

        label = value;
        return true;
    }

    private static string[] SplitValues(string value) =>
        value.Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private readonly record struct SourceLine(string Content, string Comment);

    private sealed class InterfaceBuilder
    {
        private readonly List<string> _addresses = [];
        private readonly List<string> _dns = [];
        private readonly List<string> _preUp = [];
        private readonly List<string> _postUp = [];
        private readonly List<string> _preDown = [];
        private readonly List<string> _postDown = [];
        private SecretKey _privateKey = SecretKey.Empty;
        private int? _listenPort;
        private int? _mtu;
        private string? _table;
        private string? _fwMark;
        private bool? _saveConfig;

        internal static InterfaceBuilder Empty => new();

        internal CoreError? Apply(string key, string value, int lineNumber)
        {
            switch (key.ToLowerInvariant())
            {
                case "privatekey":
                    _privateKey = SecretKey.FromValue(value);
                    return null;

                case "address":
                    _addresses.AddRange(SplitValues(value));
                    return null;

                case "dns":
                    _dns.AddRange(SplitValues(value));
                    return null;

                case "listenport":
                    return TryReadPort(value, lineNumber, out _listenPort);

                case "mtu":
                    return TryReadMtu(value, lineNumber, out _mtu);

                case "table":
                    _table = value;
                    return null;

                case "fwmark":
                    _fwMark = value;
                    return null;

                case "saveconfig":
                    return TryReadBoolean(value, lineNumber, out _saveConfig);

                case "preup":
                    _preUp.Add(value);
                    return null;

                case "postup":
                    _postUp.Add(value);
                    return null;

                case "predown":
                    _preDown.Add(value);
                    return null;

                case "postdown":
                    _postDown.Add(value);
                    return null;

                default:
                    return Unsupported(lineNumber);
            }
        }

        internal InterfaceSettings Build() => new()
        {
            PrivateKey = _privateKey,
            Addresses = _addresses.ToArray(),
            Dns = _dns.ToArray(),
            ListenPort = _listenPort,
            Mtu = _mtu,
            Table = _table,
            FwMark = _fwMark,
            SaveConfig = _saveConfig,
            PreUp = _preUp.ToArray(),
            PostUp = _postUp.ToArray(),
            PreDown = _preDown.ToArray(),
            PostDown = _postDown.ToArray(),
        };
    }

    private sealed class PeerBuilder
    {
        private readonly List<string> _allowedIps = [];
        private string _publicKey = string.Empty;
        private SecretKey? _presharedKey;
        private string? _endpoint;
        private int? _keepalive;

        internal string? Label { get; set; }

        internal bool HasAnyKey { get; private set; }

        internal CoreError? Apply(string key, string value, int lineNumber)
        {
            HasAnyKey = true;

            switch (key.ToLowerInvariant())
            {
                case "publickey":
                    _publicKey = value;
                    return null;

                case "presharedkey":
                    _presharedKey = SecretKey.FromValue(value);
                    return null;

                case "allowedips":
                    _allowedIps.AddRange(SplitValues(value));
                    return null;

                case "endpoint":
                    _endpoint = value;
                    return null;

                case "persistentkeepalive":
                    return TryReadKeepalive(value, lineNumber, out _keepalive);

                default:
                    return Unsupported(lineNumber);
            }
        }

        internal PeerConfiguration Build() => new()
        {
            PublicKey = _publicKey,
            PresharedKey = _presharedKey,
            AllowedIPs = _allowedIps.ToArray(),
            Endpoint = _endpoint,
            PersistentKeepalive = _keepalive,
            Label = Label,
        };
    }

    private static CoreError Unsupported(int lineNumber) =>
        WireGuardErrors.Configuration(
            WireGuardErrorCodes.ConfigUnsupportedKey,
            $"Line {lineNumber}: the configuration key is not one wg-quick defines.");

    private static CoreError? TryReadPort(string value, int lineNumber, out int? port)
    {
        port = null;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            return WireGuardErrors.Configuration(
                WireGuardErrorCodes.ConfigParseError,
                $"Line {lineNumber}: ListenPort '{value}' is not a number.");
        }

        port = parsed;
        return null;
    }

    private static CoreError? TryReadMtu(string value, int lineNumber, out int? mtu)
    {
        mtu = null;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            return WireGuardErrors.Configuration(
                WireGuardErrorCodes.ConfigParseError,
                $"Line {lineNumber}: MTU '{value}' is not a number.");
        }

        mtu = parsed;
        return null;
    }

    private static CoreError? TryReadKeepalive(string value, int lineNumber, out int? keepalive)
    {
        keepalive = null;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            return WireGuardErrors.Configuration(
                WireGuardErrorCodes.ConfigParseError,
                $"Line {lineNumber}: PersistentKeepalive '{value}' is not a number.");
        }

        keepalive = parsed;
        return null;
    }

    private static CoreError? TryReadBoolean(string value, int lineNumber, out bool? flag)
    {
        flag = null;
        if (bool.TryParse(value, out var parsed))
        {
            flag = parsed;
            return null;
        }

        return WireGuardErrors.Configuration(
            WireGuardErrorCodes.ConfigParseError,
            $"Line {lineNumber}: SaveConfig '{value}' is not true or false.");
    }
}
