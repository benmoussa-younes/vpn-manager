using System.Globalization;
using Core.Results;
using VpnManager.Core.Model;
using VpnManager.Core.Storage;
using VpnManager.Core.Validation;

namespace VpnManager.Core.Runtime;

/// <summary>
/// The kernel counters over <c>sysfs</c>. The two files are world-readable, so a
/// sample needs no privilege and costs a file read rather than a process; that is
/// what makes the transfer panel refresh every second without prompting.
/// </summary>
public sealed class SysfsNetworkCounterReader : INetworkCounterReader
{
    private const string DefaultBaseDirectory = "/sys/class/net";

    private readonly IConfigFileSystem _fileSystem;

    /// <summary>Initializes the reader.</summary>
    /// <param name="fileSystem">The file-system seam.</param>
    /// <param name="baseDirectory">The counter directory; defaults to <c>sysfs</c>.</param>
    public SysfsNetworkCounterReader(IConfigFileSystem fileSystem, string baseDirectory = DefaultBaseDirectory)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        _fileSystem = fileSystem;
        BaseDirectory = baseDirectory;
    }

    /// <summary>Gets the directory the counters are read from.</summary>
    public string BaseDirectory { get; }

    /// <inheritdoc />
    public Result<NetworkCounters> Read(string interfaceName)
    {
        if (!TunnelNameRules.IsValid(interfaceName))
        {
            return Result<NetworkCounters>.Failure(WireGuardErrors.Validation(
                WireGuardErrorCodes.TunnelNameInvalid,
                $"'{interfaceName}' is not a usable interface name."));
        }

        var received = ReadCounter(interfaceName, "rx_bytes");
        if (!received.TryGetValue(out var receivedBytes))
        {
            return Result<NetworkCounters>.Failure(received.Error);
        }

        var sent = ReadCounter(interfaceName, "tx_bytes");
        if (!sent.TryGetValue(out var sentBytes))
        {
            return Result<NetworkCounters>.Failure(sent.Error);
        }

        return Result<NetworkCounters>.Success(new NetworkCounters(receivedBytes, sentBytes));
    }

    private Result<long> ReadCounter(string interfaceName, string counter)
    {
        var path = Path.Combine(BaseDirectory, interfaceName, "statistics", counter);
        var read = _fileSystem.ReadAllText(path);
        if (!read.TryGetValue(out var text))
        {
            return Result<long>.Failure(read.Error);
        }

        return long.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? Result<long>.Success(value)
            : Result<long>.Failure(WireGuardErrors.Configuration(
                WireGuardErrorCodes.CountersUnavailable,
                $"'{path}' does not hold a byte count."));
    }
}
