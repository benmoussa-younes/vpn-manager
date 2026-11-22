using Core.Results;

namespace VpnManager.Core.Runtime;

/// <summary>
/// Reads the kernel's throughput counters for an interface. It exists so the
/// real-time transfer does not depend on <c>wg show</c>: the counters are cumulative
/// and world-readable, so the application can sample them without a privilege and
/// compute a rate from consecutive samples.
/// </summary>
public interface INetworkCounterReader
{
    /// <summary>Reads the cumulative counters of one interface.</summary>
    /// <param name="interfaceName">The interface name.</param>
    /// <returns>The counters, or a failure when the interface is unknown.</returns>
    Result<NetworkCounters> Read(string interfaceName);
}
