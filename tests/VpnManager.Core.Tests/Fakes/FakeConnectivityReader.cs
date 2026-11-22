using VpnManager.Core.NetworkManager;

namespace VpnManager.Core.Tests.Fakes;

/// <summary>A connectivity reader whose value the test chooses.</summary>
internal sealed class FakeConnectivityReader : IConnectivityReader
{
    /// <summary>Gets or sets the value the next read returns.</summary>
    internal NetworkConnectivity Connectivity { get; set; } = NetworkConnectivity.Full;

    /// <inheritdoc />
    public Task<NetworkConnectivity> ReadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Connectivity);
}
