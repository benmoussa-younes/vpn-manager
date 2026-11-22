using System.Text;

namespace VpnManager.Core.NetworkManager;

/// <summary>One row of <c>nmcli -t -f NAME,TYPE,ACTIVE connection show</c>.</summary>
/// <param name="Name">The connection id.</param>
/// <param name="Type">The connection type, e.g. <c>wireguard</c>.</param>
/// <param name="IsActive">Whether NetworkManager reports it active.</param>
public sealed record NetworkManagerConnectionEntry(string Name, string Type, bool IsActive);

/// <summary>
/// Parses the terse (<c>-t</c>) output of <c>nmcli</c>. It is pure, so the exact
/// escaping rules are unit-tested without a NetworkManager on the machine.
/// </summary>
public static class NetworkManagerConnectionParser
{
    /// <summary>Parses the connection list into entries.</summary>
    /// <param name="output">The standard output of the list command.</param>
    /// <returns>The entries; a malformed line is skipped.</returns>
    public static IReadOnlyList<NetworkManagerConnectionEntry> ParseConnectionList(string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var entries = new List<NetworkManagerConnectionEntry>();

        foreach (var line in Lines(output))
        {
            var fields = SplitTerse(line);
            if (fields.Count < 3)
            {
                continue;
            }

            entries.Add(new NetworkManagerConnectionEntry(fields[0], fields[1], ParseYesNo(fields[2])));
        }

        return entries;
    }

    /// <summary>Reads one <c>key:value</c> property from a detail listing.</summary>
    /// <param name="output">The standard output of the detail command.</param>
    /// <param name="key">The property name, e.g. <c>connection.interface-name</c>.</param>
    /// <returns>The value, or <see langword="null"/> when the key is absent.</returns>
    public static string? ReadProperty(string output, string key)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var prefix = string.Concat(key, ":");
        foreach (var line in Lines(output))
        {
            if (line.StartsWith(prefix, StringComparison.Ordinal))
            {
                return line[prefix.Length..].Trim();
            }
        }

        return null;
    }

    /// <summary>Reads the <c>connection.permissions</c> entries.</summary>
    /// <param name="output">The standard output of the detail command.</param>
    /// <returns>The entries, e.g. <c>user:alice</c>; empty when unset.</returns>
    public static IReadOnlyList<string> ReadPermissions(string output)
    {
        var value = ReadProperty(output, "connection.permissions");
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>Splits one terse line on unescaped colons.</summary>
    /// <param name="line">The line.</param>
    /// <returns>The fields, with <c>\:</c> and <c>\\</c> unescaped.</returns>
    public static IReadOnlyList<string> SplitTerse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var fields = new List<string>();
        var current = new StringBuilder();

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '\\' && index + 1 < line.Length)
            {
                current.Append(line[++index]);
                continue;
            }

            if (character == ':')
            {
                fields.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        fields.Add(current.ToString());
        return fields;
    }

    private static bool ParseYesNo(string value) => string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);

    private static string[] Lines(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
