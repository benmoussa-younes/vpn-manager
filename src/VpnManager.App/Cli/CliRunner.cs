using System.Text.Json;
using System.Text.Json.Serialization;
using VpnManager.Core.Model;
using VpnManager.Core.Runtime;
using VpnManager.Core.Services;
using VpnManager.Core.State;

namespace VpnManager.App.Cli;

internal sealed record CliTunnel(
    string Name,
    string State,
    string Source,
    bool Configured,
    int Peers,
    bool AutoReconnect,
    string DnsMode,
    IReadOnlyList<string> DnsSearchDomains,
    string? DnsContainment,
    IReadOnlyList<string> DnsServers,
    IReadOnlyList<string> DnsDomains);

internal sealed record CliStatus(string Version, string ConfigDirectory, IReadOnlyList<CliTunnel> Tunnels);

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(CliStatus))]
internal sealed partial class CliJsonContext : JsonSerializerContext
{
}

/// <summary>
/// The headless command implementation. It receives the domain service and writes
/// only text or source-generated JSON, so it is testable without a display.
/// </summary>
internal sealed class CliRunner
{
    private readonly TunnelService _service;
    private readonly string _configDirectory;

    internal CliRunner(TunnelService service, string configDirectory)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentException.ThrowIfNullOrWhiteSpace(configDirectory);

        _service = service;
        _configDirectory = configDirectory;
    }

    internal async Task<int> RunAsync(CliOptions options, TextWriter output, TextWriter error, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (options.Error is { } message)
        {
            error.WriteLine(message);
            return 2;
        }

        switch (options.Command)
        {
            case CliCommand.Status:
                return await StatusAsync(options.Json, output, cancellationToken).ConfigureAwait(false);
            case CliCommand.Connect:
                return await ChangeAsync(options.Name!, connect: true, output, cancellationToken).ConfigureAwait(false);
            case CliCommand.Disconnect:
                return await ChangeAsync(options.Name!, connect: false, output, cancellationToken).ConfigureAwait(false);
            case CliCommand.Policy:
                return await SetPolicyAsync(options, output, cancellationToken).ConfigureAwait(false);
            default:
                error.WriteLine("No command was requested.");
                return 2;
        }
    }

    private async Task<int> StatusAsync(bool json, TextWriter output, CancellationToken cancellationToken)
    {
        var listed = await _service.ListTunnelsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!listed.TryGetValue(out var rows))
        {
            output.WriteLine(listed.Error.Message);
            return 1;
        }

        var tunnels = new List<CliTunnel>(rows.Count);
        foreach (var row in rows)
        {
            var policy = _service.GetPolicy(row.Name);
            DnsState? dns = null;
            if (row.State == TunnelState.Up)
            {
                var described = await _service.DescribeDnsAsync(row.Name, cancellationToken).ConfigureAwait(false);
                dns = described.TryGetValue(out var state) ? state : null;
            }

            tunnels.Add(new CliTunnel(
                row.Name,
                row.State.ToString(),
                row.Source.ToString(),
                row.HasConfiguration,
                row.PeerCount,
                policy.AutoReconnect,
                policy.DnsMode.ToString(),
                policy.SearchDomains,
                dns is null ? null : DnsContainmentPolicy.Classify(dns).ToString(),
                dns?.Servers ?? [],
                dns?.Domains ?? []));
        }

        var status = new CliStatus(Program.Version, _configDirectory, tunnels);
        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(status, CliJsonContext.Default.CliStatus));
            return 0;
        }

        output.WriteLine($"vpn-manager {status.Version}");
        output.WriteLine($"configuration: {status.ConfigDirectory}");
        foreach (var tunnel in status.Tunnels)
        {
            output.WriteLine(
                $"{tunnel.Name}\t{tunnel.State}\t{tunnel.Source}\tauto={tunnel.AutoReconnect}\tdns={tunnel.DnsMode}\tcontainment={tunnel.DnsContainment ?? "unknown"}");
        }

        return 0;
    }

    private async Task<int> ChangeAsync(string name, bool connect, TextWriter output, CancellationToken cancellationToken)
    {
        var result = connect
            ? await _service.StartAsync(name, cancellationToken).ConfigureAwait(false)
            : await _service.StopAsync(name, cancellationToken).ConfigureAwait(false);

        if (result.TryGetValue(out var value))
        {
            output.WriteLine(value);
            return 0;
        }

        output.WriteLine(result.Error.Message);
        return 1;
    }

    private async Task<int> SetPolicyAsync(CliOptions options, TextWriter output, CancellationToken cancellationToken)
    {
        var current = _service.GetPolicy(options.Name!);
        var policy = new TunnelPolicy(
            options.AutoReconnect ?? current.AutoReconnect,
            options.DnsMode ?? current.DnsMode,
            options.DnsSearchDomains ?? current.SearchDomains);

        var saved = await _service.SetPolicyAsync(options.Name!, policy, cancellationToken).ConfigureAwait(false);
        if (saved.TryGetValue(out var value))
        {
            output.WriteLine($"{options.Name}: auto-reconnect={value.AutoReconnect} dns={value.DnsMode}");
            return 0;
        }

        output.WriteLine(saved.Error.Message);
        return 1;
    }
}
