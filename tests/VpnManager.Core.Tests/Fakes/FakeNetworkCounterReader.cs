using Core.Results;
using VpnManager.Core.Model;
using VpnManager.Core.Runtime;

namespace VpnManager.Core.Tests.Fakes;

/// <summary>An in-memory kernel counter reader, so a transfer rate is deterministic.</summary>
internal sealed class FakeNetworkCounterReader : INetworkCounterReader
{
    /// <summary>Counters keyed by interface name.</summary>
    internal Dictionary<string, NetworkCounters> Counters { get; } = new(StringComparer.Ordinal);

    public Result<NetworkCounters> Read(string interfaceName) =>
        Counters.TryGetValue(interfaceName, out var value)
            ? Result<NetworkCounters>.Success(value)
            : Result<NetworkCounters>.Failure(WireGuardErrors.Configuration(
                WireGuardErrorCodes.CountersUnavailable,
                $"no counters for '{interfaceName}'."));
}
