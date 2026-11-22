namespace VpnManager.Core.Runtime;

/// <summary>
/// Reads the text of <c>resolvectl status &lt;link&gt;</c> into a
/// <see cref="DnsState"/>. It is pure, so the containment verdict is tested without a
/// resolver.
/// </summary>
public static class ResolvectlOutputParser
{
    private const string ServersPrefix = "DNS Servers:";
    private const string DomainPrefix = "DNS Domain:";
    private const string DefaultRoutePrefix = "Default Route:";

    /// <summary>Parses the status text.</summary>
    /// <param name="text">The standard output of <c>resolvectl status</c>.</param>
    /// <param name="state">The parsed state.</param>
    /// <returns><see langword="true"/> when at least one DNS line was found.</returns>
    public static bool TryParse(string? text, out DnsState state)
    {
        state = null!;

        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var servers = new List<string>();
        var domains = new List<string>();
        var defaultRoute = false;
        var found = false;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();

            if (line.StartsWith(ServersPrefix, StringComparison.Ordinal))
            {
                servers.AddRange(Split(line[ServersPrefix.Length..]));
                found = true;
            }
            else if (line.StartsWith(DomainPrefix, StringComparison.Ordinal))
            {
                domains.AddRange(Split(line[DomainPrefix.Length..]));
                found = true;
            }
            else if (line.StartsWith(DefaultRoutePrefix, StringComparison.Ordinal))
            {
                defaultRoute = line[DefaultRoutePrefix.Length..].Trim().Equals("yes", StringComparison.OrdinalIgnoreCase);
                found = true;
            }
        }

        state = new DnsState(servers, domains, defaultRoute);
        return found;
    }

    private static string[] Split(string value) =>
        value.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
