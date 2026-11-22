using Core.Results;
using VpnManager.Core.Model;
using VpnManager.Core.Runtime;

namespace VpnManager.Core.Tests.Fakes;

/// <summary>A resolver-state reader whose answer the test chooses.</summary>
internal sealed class FakeDnsStateReader : IDnsStateReader
{
    /// <summary>The state returned; when <see langword="null"/> the read fails.</summary>
    internal DnsState? State { get; set; }

    /// <summary>The failure returned, when set.</summary>
    internal CoreError? Error { get; set; }

    /// <summary>The interfaces the reader was asked about.</summary>
    internal List<string> Read { get; } = [];

    public Task<Result<DnsState>> ReadAsync(string interfaceName, CancellationToken cancellationToken = default)
    {
        Read.Add(interfaceName);

        return Task.FromResult(Error is not null
            ? Result<DnsState>.Failure(Error)
            : State is not null
                ? Result<DnsState>.Success(State)
                : Result<DnsState>.Failure(WireGuardErrors.DnsUnavailable()));
    }
}
