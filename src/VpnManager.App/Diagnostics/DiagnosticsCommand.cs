using System.Globalization;
using VpnManager.Core.Model;
using VpnManager.Core.Runtime;

namespace VpnManager.App.Diagnostics;

/// <summary>
/// The display-free path of the published binary. It never touches Avalonia, so
/// it runs on a headless machine; it is how the Native AOT build is started and
/// checked when no window can be rendered.
/// </summary>
public static class DiagnosticsCommand
{
    /// <summary>The command-line flag that selects this path.</summary>
    public const string Flag = "--diagnostics";

    /// <summary>Decides whether the flag is present.</summary>
    /// <param name="arguments">The command-line arguments.</param>
    /// <returns><see langword="true"/> when diagnostics were requested.</returns>
    public static bool IsRequested(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return arguments.Contains(Flag, StringComparer.Ordinal);
    }

    /// <summary>
    /// Reports what the process can observe: the configuration directory, the
    /// kernel interfaces and the tunnels of the per-user configuration.
    /// </summary>
    /// <param name="output">Where to write.</param>
    /// <param name="root">The composed object graph.</param>
    /// <returns>Always 0: a missing directory is a finding, not a failure.</returns>
    public static async Task<int> RunAsync(TextWriter output, CompositionRoot root)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(root);

        output.WriteLine("VPN Manager - diagnostics");
        output.WriteLine($"configuration directory: {root.Store.ConfigDirectory}");
        output.WriteLine($"executables: {WireGuardCommand.WireGuardExecutable}");

        var prerequisite = root.RoutingPrerequisite.Read();
        output.WriteLine(prerequisite is null
            ? "full-tunnel prerequisite: unknown"
            : prerequisite.IsSatisfied
                ? "full-tunnel prerequisite: satisfied"
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"full-tunnel prerequisite: MISSING (src_valid_mark={prerequisite.SrcValidMark}, rp_filter={prerequisite.RpFilter}) - {prerequisite.Remediation}"));

        var names = await root.Runtime.ListInterfaceNamesAsync().ConfigureAwait(false);
        output.WriteLine(names.TryGetValue(out var interfaces)
            ? string.Create(CultureInfo.InvariantCulture, $"kernel interfaces: {(interfaces.Count == 0 ? "(none)" : string.Join(' ', interfaces))}")
            : $"kernel interfaces: unavailable ({names.Error.Message})");

        if (names.TryGetValue(out var interfaceNames))
        {
            foreach (var name in interfaceNames)
            {
                var counters = root.CounterReader.Read(name);
                output.WriteLine(counters.TryGetValue(out var sample)
                    ? string.Create(CultureInfo.InvariantCulture, $"counters {name}: {sample.ReceivedBytes} B received, {sample.SentBytes} B sent")
                    : $"counters {name}: unavailable ({counters.Error.Message})");
            }
        }

        var tunnels = await root.Service.ListTunnelsAsync().ConfigureAwait(false);
        if (tunnels.TryGetValue(out var rows))
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"tunnels: {rows.Count}"));
            foreach (var row in rows)
            {
                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  {row.Name}: state={row.State} configured={row.HasConfiguration} peers={row.PeerCount}"));

                if (row.Note is not null)
                {
                    output.WriteLine($"    note: {row.Note}");
                }

                if (row.Source == TunnelSource.Configuration)
                {
                    var ipv6 = root.Service.DescribeIpv6(row.Name);
                    output.WriteLine(ipv6.TryGetValue(out var description)
                        ? string.Create(CultureInfo.InvariantCulture, $"    ipv6: {description.Profile} (host IPv6: {(description.HostHasIpv6 ? "yes" : "no")})")
                        : $"    ipv6: unknown ({ipv6.Error.Message})");
                }

                if (row.State == TunnelState.Up)
                {
                    var dns = await root.Service.DescribeDnsAsync(row.Name).ConfigureAwait(false);
                    if (dns.TryGetValue(out var dnsState))
                    {
                        var containment = DnsContainmentPolicy.Classify(dnsState);
                        output.WriteLine(string.Create(
                            CultureInfo.InvariantCulture,
                            $"    dns: {containment} - {DnsContainmentPolicy.Describe(containment)}"));
                    }
                    else
                    {
                        output.WriteLine($"    dns: unknown ({dns.Error.Message})");
                    }
                }
            }
        }
        else
        {
            output.WriteLine($"tunnels: unavailable ({tunnels.Error.Message})");
        }

        output.WriteLine("result: ok");
        return 0;
    }
}
