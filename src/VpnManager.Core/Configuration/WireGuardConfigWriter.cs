using System.Globalization;
using System.Text;
using VpnManager.Core.Model;

namespace VpnManager.Core.Configuration;

/// <summary>
/// Writes a <see cref="TunnelConfiguration"/> back to wg-quick text. This is the
/// only production type besides the validator that reads the raw private key.
/// </summary>
/// <remarks>
/// The output is the canonical order the format documents, with one
/// <c>Key = Value</c> line per value so a diff of two files is readable.
/// </remarks>
public static class WireGuardConfigWriter
{
    /// <summary>
    /// Renders the configuration as wg-quick text, terminated by a newline.
    /// </summary>
    /// <param name="configuration">The configuration to render.</param>
    /// <returns>The configuration text.</returns>
    public static string Write(TunnelConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var builder = new StringBuilder();
        var settings = configuration.Interface;

        builder.Append("[Interface]\n");
        builder.Append("# Name = ").Append(configuration.Name).Append('\n');

        foreach (var address in settings.Addresses)
        {
            WritePair(builder, "Address", address);
        }

        foreach (var dns in settings.Dns)
        {
            WritePair(builder, "DNS", dns);
        }

        if (settings.Mtu is { } mtu)
        {
            WritePair(builder, "MTU", mtu.ToString(CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrEmpty(settings.Table))
        {
            WritePair(builder, "Table", settings.Table);
        }

        if (!settings.PrivateKey.IsEmpty)
        {
            WritePair(builder, "PrivateKey", settings.PrivateKey.Reveal());
        }

        if (settings.ListenPort is { } listenPort)
        {
            WritePair(builder, "ListenPort", listenPort.ToString(CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrEmpty(settings.FwMark))
        {
            WritePair(builder, "FwMark", settings.FwMark);
        }

        if (settings.SaveConfig is { } saveConfig)
        {
            WritePair(builder, "SaveConfig", saveConfig ? "true" : "false");
        }

        foreach (var hook in settings.PreUp)
        {
            WritePair(builder, "PreUp", hook);
        }

        foreach (var hook in settings.PostUp)
        {
            WritePair(builder, "PostUp", hook);
        }

        foreach (var hook in settings.PreDown)
        {
            WritePair(builder, "PreDown", hook);
        }

        foreach (var hook in settings.PostDown)
        {
            WritePair(builder, "PostDown", hook);
        }

        foreach (var peer in configuration.Peers)
        {
            builder.Append('\n');
            builder.Append("[Peer]\n");

            if (!string.IsNullOrWhiteSpace(peer.Label))
            {
                builder.Append("# Name = ").Append(peer.Label).Append('\n');
            }

            if (!string.IsNullOrEmpty(peer.PublicKey))
            {
                WritePair(builder, "PublicKey", peer.PublicKey);
            }

            if (peer.PresharedKey is { IsEmpty: false } presharedKey)
            {
                WritePair(builder, "PresharedKey", presharedKey.Reveal());
            }

            foreach (var allowed in peer.AllowedIPs)
            {
                WritePair(builder, "AllowedIPs", allowed);
            }

            if (!string.IsNullOrWhiteSpace(peer.Endpoint))
            {
                WritePair(builder, "Endpoint", peer.Endpoint);
            }

            if (peer.PersistentKeepalive is { } keepalive)
            {
                WritePair(builder, "PersistentKeepalive", keepalive.ToString(CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }

    private static void WritePair(StringBuilder builder, string key, string value)
    {
        builder.Append(key).Append(" = ").Append(value).Append('\n');
    }
}
