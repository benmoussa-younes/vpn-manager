using VpnManager.Core.NetworkManager;

namespace VpnManager.App.Cli;

internal enum CliCommand
{
    None = 0,
    Status = 1,
    Connect = 2,
    Disconnect = 3,
    Policy = 4,
    FixSysctl = 5,
}

/// <summary>
/// The headless command line: status, connect, disconnect and the per-profile
/// auto-reconnect/DNS policy. It never starts Avalonia.
/// </summary>
internal sealed record CliOptions(
    CliCommand Command,
    bool Json = false,
    string? Name = null,
    bool? AutoReconnect = null,
    NetworkManagerDnsMode? DnsMode = null,
    IReadOnlyList<string>? DnsSearchDomains = null,
    string? Error = null)
{
    internal static bool IsRequested(string[] arguments) =>
        arguments.Any(argument => argument is "--status" or "--connect" or "--disconnect" or "--policy" or "--fix-sysctl");

    internal static CliOptions Parse(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var command = CliCommand.None;
        var json = arguments.Contains("--json", StringComparer.Ordinal);
        string? name = null;
        bool? autoReconnect = null;
        NetworkManagerDnsMode? dnsMode = null;
        IReadOnlyList<string>? searchDomains = null;

        if (arguments.Contains("--status", StringComparer.Ordinal))
        {
            command = CliCommand.Status;
        }
        else if (arguments.Contains("--fix-sysctl", StringComparer.Ordinal))
        {
            command = CliCommand.FixSysctl;
        }
        else if (arguments.Contains("--connect", StringComparer.Ordinal))
        {
            command = CliCommand.Connect;
            name = ReadValue(arguments, "--connect");
        }
        else if (arguments.Contains("--disconnect", StringComparer.Ordinal))
        {
            command = CliCommand.Disconnect;
            name = ReadValue(arguments, "--disconnect");
        }
        else if (arguments.Contains("--policy", StringComparer.Ordinal))
        {
            command = CliCommand.Policy;
            name = ReadValue(arguments, "--policy");
        }

        if (command == CliCommand.None)
        {
            return new CliOptions(CliCommand.None, Error: "No command was requested.");
        }

        if (command is (CliCommand.Connect or CliCommand.Disconnect or CliCommand.Policy) &&
            string.IsNullOrWhiteSpace(name))
        {
            return new CliOptions(command, json, Error: "The command needs a profile name.");
        }

        if (TryValue(arguments, "--auto-reconnect", out var autoText))
        {
            autoReconnect = autoText switch
            {
                "on" or "true" or "yes" => true,
                "off" or "false" or "no" => false,
                _ => null,
            };

            if (autoReconnect is null)
            {
                return new CliOptions(command, json, name, Error: "--auto-reconnect expects on or off.");
            }
        }

        if (TryValue(arguments, "--dns-mode", out var modeText))
        {
            dnsMode = modeText.ToLowerInvariant() switch
            {
                "profile" => NetworkManagerDnsMode.Profile,
                "system" => NetworkManagerDnsMode.System,
                "split" => NetworkManagerDnsMode.Split,
                _ => null,
            };

            if (dnsMode is null)
            {
                return new CliOptions(command, json, name, Error: "--dns-mode expects profile, system or split.");
            }
        }

        var searchIndex = Array.IndexOf(arguments, "--dns-search");
        if (searchIndex >= 0)
        {
            if (searchIndex + 1 >= arguments.Length ||
                arguments[searchIndex + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return new CliOptions(command, json, name, Error: "--dns-search expects a comma-separated list, or an empty value to clear it.");
            }

            // An explicit empty value is a clear, not a missing option: the stored
            // list is replaced with nothing.
            searchDomains = arguments[searchIndex + 1]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        if (command == CliCommand.Policy && autoReconnect is null && dnsMode is null && searchDomains is null)
        {
            return new CliOptions(command, json, name, Error: "--policy needs --auto-reconnect, --dns-mode or --dns-search.");
        }

        return new CliOptions(command, json, name, autoReconnect, dnsMode, searchDomains);
    }

    private static string? ReadValue(string[] arguments, string option)
    {
        var index = Array.IndexOf(arguments, option);
        return index >= 0 && index + 1 < arguments.Length && !arguments[index + 1].StartsWith("--", StringComparison.Ordinal)
            ? arguments[index + 1]
            : null;
    }

    private static bool TryValue(string[] arguments, string option, out string value)
    {
        value = ReadValue(arguments, option) ?? string.Empty;
        return value.Length > 0;
    }
}
