using Core.Results;
using VpnManager.Core.Model;
using VpnManager.Core.Runtime;

namespace VpnManager.Core.Tests.Fakes;

/// <summary>
/// A runtime reader whose observations are chosen by the test, so no privileged
/// command is ever needed and the root-only path is exercised as a value.
/// </summary>
internal sealed class FakeWireGuardRuntimeInspector : IWireGuardRuntimeInspector
{
    /// <summary>The names <c>wg show interfaces</c> would report.</summary>
    internal List<string> InterfaceNames { get; } = [];

    /// <summary>Interfaces keyed by name, for the live read.</summary>
    internal Dictionary<string, WireGuardInterface> Interfaces { get; } = new(StringComparer.Ordinal);

    /// <summary>When set, the name listing fails with this error.</summary>
    internal CoreError? NamesError { get; set; }

    /// <summary>When set, the live read fails with this error.</summary>
    internal CoreError? LiveError { get; set; }

    /// <summary>The public key derivation result.</summary>
    internal string DerivedPublicKey { get; set; } = TestData.DerivedPublicKey;

    /// <summary>When set, the derivation fails with this error.</summary>
    internal CoreError? DeriveError { get; set; }

    /// <summary>The private key the generator returns.</summary>
    internal SecretKey GeneratedPrivateKey { get; set; } = SecretKey.FromValue(TestData.InterfacePrivateKey);

    /// <summary>When set, generation fails with this error.</summary>
    internal CoreError? GenerateError { get; set; }

    /// <summary>The private keys the derivation was asked about.</summary>
    internal List<SecretKey> DerivedFrom { get; } = [];

    public Task<Result<IReadOnlyList<string>>> ListInterfaceNamesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(NamesError is null
            ? Result<IReadOnlyList<string>>.Success(InterfaceNames.ToArray())
            : Result<IReadOnlyList<string>>.Failure(NamesError));

    public Task<Result<IReadOnlyList<WireGuardInterface>>> ListInterfacesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<IReadOnlyList<WireGuardInterface>>.Success(Interfaces.Values.ToArray()));

    public Task<Result<WireGuardInterface>> GetInterfaceAsync(string name, CancellationToken cancellationToken = default)
    {
        if (LiveError is not null)
        {
            return Task.FromResult(Result<WireGuardInterface>.Failure(LiveError));
        }

        return Task.FromResult(Interfaces.TryGetValue(name, out var value)
            ? Result<WireGuardInterface>.Success(value)
            : Result<WireGuardInterface>.Failure(WireGuardErrors.InterfaceNotFound(name)));
    }

    public Task<Result<SecretKey>> GeneratePrivateKeyAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(GenerateError is null
            ? Result<SecretKey>.Success(GeneratedPrivateKey)
            : Result<SecretKey>.Failure(GenerateError));

    public Task<Result<SecretKey>> GeneratePresharedKeyAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(GenerateError is null
            ? Result<SecretKey>.Success(GeneratedPrivateKey)
            : Result<SecretKey>.Failure(GenerateError));

    public Task<Result<string>> DerivePublicKeyAsync(SecretKey privateKey, CancellationToken cancellationToken = default)
    {
        DerivedFrom.Add(privateKey);
        return Task.FromResult(DeriveError is null
            ? Result<string>.Success(DerivedPublicKey)
            : Result<string>.Failure(DeriveError));
    }
}
