using Core.Results;
using VpnManager.Core.Model;
using VpnManager.Core.Validation;

namespace VpnManager.Core.Presentation;

/// <summary>
/// Turns the editor's text fields into a validated
/// <see cref="TunnelConfiguration"/>. Parsing a field is presentation-adjacent
/// policy (what does an empty box mean?), so it lives here rather than in the
/// view, and it is unit-tested without a view.
/// </summary>
public static class EditorInputMapper
{
    public static Result<TunnelConfiguration> ToConfiguration(EditorInput input, SecretKey privateKey) =>
        ToConfiguration(input, privateKey, []);

    /// <summary>
    /// Builds and validates a configuration from editor text, using the key the
    /// presenter holds when the operator did not type a new one.
    /// </summary>
    /// <param name="input">The editor text.</param>
    /// <param name="privateKey">The key the presenter holds, always non-null; it may be empty.</param>
    /// <param name="resolvedPresharedKeys">
    /// One entry per peer: the preshared key the presenter already holds for that
    /// row, used when the operator left the field blank. It is passed by value
    /// rather than read back out of the view, so a loaded secret is never
    /// rendered and then re-parsed.
    /// </param>
    /// <returns>The validated configuration, or a failure.</returns>
    public static Result<TunnelConfiguration> ToConfiguration(
        EditorInput input,
        SecretKey privateKey,
        IReadOnlyList<SecretKey?> resolvedPresharedKeys)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(privateKey);
        ArgumentNullException.ThrowIfNull(resolvedPresharedKeys);

        var typedKey = input.PrivateKey.Trim();
        var effectiveKey = typedKey.Length > 0 ? SecretKey.FromValue(typedKey) : privateKey;

        var listenPort = ParseOptionalInteger(input.ListenPort, WireGuardErrorCodes.ListenPortInvalid, "ListenPort");
        if (!listenPort.IsSuccess)
        {
            return Result<TunnelConfiguration>.Failure(listenPort.Error);
        }

        var mtu = ParseOptionalInteger(input.Mtu, WireGuardErrorCodes.MtuInvalid, "MTU");
        if (!mtu.IsSuccess)
        {
            return Result<TunnelConfiguration>.Failure(mtu.Error);
        }

        var peers = new List<PeerConfiguration>(input.Peers.Count);
        for (var index = 0; index < input.Peers.Count; index++)
        {
            var peer = input.Peers[index];
            var keepalive = ParseOptionalInteger(peer.PersistentKeepalive, WireGuardErrorCodes.KeepaliveInvalid, "PersistentKeepalive");
            if (!keepalive.IsSuccess)
            {
                return Result<TunnelConfiguration>.Failure(keepalive.Error);
            }

            var typedPresharedKey = peer.PresharedKey.Trim();
            var presharedKey = typedPresharedKey.Length > 0
                ? SecretKey.FromValue(typedPresharedKey)
                : index < resolvedPresharedKeys.Count ? resolvedPresharedKeys[index] : null;

            peers.Add(new PeerConfiguration
            {
                Label = NullIfEmpty(peer.Label),
                PublicKey = peer.PublicKey.Trim(),
                PresharedKey = presharedKey,
                AllowedIPs = SplitList(peer.AllowedIps),
                Endpoint = NullIfEmpty(peer.Endpoint),
                PersistentKeepalive = keepalive.Value,
            });
        }

        var configuration = new TunnelConfiguration
        {
            Name = input.Name.Trim(),
            Interface = new InterfaceSettings
            {
                PrivateKey = effectiveKey,
                Addresses = SplitList(input.Addresses),
                Dns = SplitList(input.Dns),
                ListenPort = listenPort.Value,
                Mtu = mtu.Value,
                Table = NullIfEmpty(input.Table),
            },
            Peers = peers,
        };

        return TunnelValidator.Validate(configuration);
    }

    /// <summary>Splits editor text into values on newlines and commas.</summary>
    /// <param name="text">The editor text.</param>
    /// <returns>The non-empty, trimmed values.</returns>
    public static string[] SplitList(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return text.Split(['\n', '\r', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static Result<int?> ParseOptionalInteger(string text, string errorCode, string label)
    {
        var value = text.Trim();
        if (value.Length == 0)
        {
            return Result<int?>.Success(null);
        }

        return int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? Result<int?>.Success(parsed)
            : Result<int?>.Failure(WireGuardErrors.Validation(errorCode, $"{label} '{value}' is not a whole number."));
    }

    private static string? NullIfEmpty(string? text)
    {
        var value = text?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
