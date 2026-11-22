namespace VpnManager.Core.Runtime;

/// <summary>
/// The kernel's byte counters for one network interface, in the direction the
/// kernel counts them: received into the host, sent out of it.
/// </summary>
/// <param name="ReceivedBytes">Bytes received on the interface.</param>
/// <param name="SentBytes">Bytes sent on the interface.</param>
public sealed record NetworkCounters(long ReceivedBytes, long SentBytes);
