using Core.Results;
using VpnManager.Core.Model;

namespace VpnManager.Core.Runtime;

/// <summary>
/// Reads live WireGuard state and generates key material. Every method is a
/// failure value on failure, never an exception.
/// </summary>
public interface IWireGuardRuntimeInspector
{
    /// <summary>Lists the interface names the kernel currently reports.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The names.</returns>
    Task<Result<IReadOnlyList<string>>> ListInterfaceNamesAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads every interface with its peers and counters.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The interfaces.</returns>
    Task<Result<IReadOnlyList<WireGuardInterface>>> ListInterfacesAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads one interface with its peers and counters.</summary>
    /// <param name="name">The interface name.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The interface.</returns>
    Task<Result<WireGuardInterface>> GetInterfaceAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Generates a private key with <c>wg genkey</c>.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The key, wrapped so it cannot be printed.</returns>
    Task<Result<SecretKey>> GeneratePrivateKeyAsync(CancellationToken cancellationToken = default);

    /// <summary>Generates a preshared key with <c>wg genpsk</c>.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The key, wrapped so it cannot be printed.</returns>
    Task<Result<SecretKey>> GeneratePresharedKeyAsync(CancellationToken cancellationToken = default);

    /// <summary>Derives the public key of a private key with <c>wg pubkey</c>.</summary>
    /// <param name="privateKey">The private key.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The public key, which is safe to display.</returns>
    Task<Result<string>> DerivePublicKeyAsync(SecretKey privateKey, CancellationToken cancellationToken = default);
}

/// <summary>
/// The runtime inspector over the command runner. It contains no process
/// handling: it builds a semantic command, runs it, and parses the text.
/// </summary>
public sealed class WireGuardRuntimeInspector : IWireGuardRuntimeInspector
{
    private readonly IWireGuardCommandRunner _runner;

    /// <summary>Initializes the inspector.</summary>
    /// <param name="runner">The command boundary.</param>
    public WireGuardRuntimeInspector(IWireGuardCommandRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<string>>> ListInterfaceNamesAsync(CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunAsync(WireGuardCommand.ShowInterfaces(), cancellationToken).ConfigureAwait(false);
        return result.TryGetValue(out var output)
            ? WireGuardShowParser.ParseInterfaceNames(output.StandardOutput)
            : Result<IReadOnlyList<string>>.Failure(result.Error);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<WireGuardInterface>>> ListInterfacesAsync(CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunAsync(WireGuardCommand.ShowAll(), cancellationToken).ConfigureAwait(false);
        return result.TryGetValue(out var output)
            ? WireGuardShowParser.ParseInterfaces(output.StandardOutput)
            : Result<IReadOnlyList<WireGuardInterface>>.Failure(result.Error);
    }

    /// <inheritdoc />
    public async Task<Result<WireGuardInterface>> GetInterfaceAsync(string name, CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunAsync(WireGuardCommand.ShowInterface(name), cancellationToken).ConfigureAwait(false);
        if (!result.TryGetValue(out var output))
        {
            return Result<WireGuardInterface>.Failure(result.Error);
        }

        var parsed = WireGuardShowParser.ParseInterfaces(output.StandardOutput);
        if (!parsed.TryGetValue(out var interfaces))
        {
            return Result<WireGuardInterface>.Failure(parsed.Error);
        }

        if (interfaces.Count == 0)
        {
            return Result<WireGuardInterface>.Failure(WireGuardErrors.InterfaceNotFound(name));
        }

        var match = interfaces.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
        return Result<WireGuardInterface>.Success(match ?? interfaces[0]);
    }

    /// <inheritdoc />
    public Task<Result<SecretKey>> GeneratePrivateKeyAsync(CancellationToken cancellationToken = default) =>
        GenerateKeyAsync(WireGuardCommand.GeneratePrivateKey(), cancellationToken);

    /// <inheritdoc />
    public Task<Result<SecretKey>> GeneratePresharedKeyAsync(CancellationToken cancellationToken = default) =>
        GenerateKeyAsync(WireGuardCommand.GeneratePresharedKey(), cancellationToken);

    /// <inheritdoc />
    public async Task<Result<string>> DerivePublicKeyAsync(SecretKey privateKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(privateKey);

        var result = await _runner.RunAsync(WireGuardCommand.DerivePublicKey(privateKey), cancellationToken).ConfigureAwait(false);
        if (!result.TryGetValue(out var output))
        {
            return Result<string>.Failure(result.Error);
        }

        var publicKey = output.StandardOutput.Trim();
        return Validation.WireGuardKey.IsValid(publicKey)
            ? Result<string>.Success(publicKey)
            : Result<string>.Failure(WireGuardErrors.KeyGenerationFailed("wg pubkey did not produce a 32-byte base64 key."));
    }

    private async Task<Result<SecretKey>> GenerateKeyAsync(WireGuardCommand command, CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(command, cancellationToken).ConfigureAwait(false);
        if (!result.TryGetValue(out var output))
        {
            return Result<SecretKey>.Failure(result.Error);
        }

        var key = output.StandardOutput.Trim();

        // The failure message names the command, never the produced key.
        return Validation.WireGuardKey.IsValid(key)
            ? Result<SecretKey>.Success(SecretKey.FromValue(key))
            : Result<SecretKey>.Failure(WireGuardErrors.KeyGenerationFailed($"{command} did not produce a 32-byte base64 key."));
    }
}
