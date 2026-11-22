using VpnManager.Core.Model;

namespace VpnManager.Core.Runtime;

/// <summary>
/// A semantic WireGuard command. It is constructed only through the factories,
/// so the executable is always <c>wg</c> and no argument is ever a shell, a shell
/// command, or a privilege-escalation program.
/// </summary>
/// <remarks>
/// The optional standard input carries key material for <c>wg pubkey</c>. It is
/// deliberately excluded from <see cref="ToString"/> so a command can be logged
/// or quoted in an error message without exposing a key.
/// </remarks>
public sealed class WireGuardCommand
{
    /// <summary>The WireGuard control executable.</summary>
    public const string WireGuardExecutable = "wg";

    private WireGuardCommand(string executable, IReadOnlyList<string> arguments, string? standardInput)
    {
        Executable = executable;
        Arguments = arguments;
        StandardInput = standardInput;
    }

    /// <summary>Gets the executable name; always <c>wg</c>.</summary>
    public string Executable { get; }

    /// <summary>Gets the arguments, already split; no shell ever parses them.</summary>
    public IReadOnlyList<string> Arguments { get; }

    /// <summary>Gets the text to write to standard input, or <see langword="null"/>.</summary>
    public string? StandardInput { get; }

    /// <summary>The command that lists interface names: <c>wg show interfaces</c>.</summary>
    /// <returns>The command.</returns>
    public static WireGuardCommand ShowInterfaces() => new(WireGuardExecutable, ["show", "interfaces"], null);

    /// <summary>The command that lists every interface with its peers: <c>wg show</c>.</summary>
    /// <returns>The command.</returns>
    public static WireGuardCommand ShowAll() => new(WireGuardExecutable, ["show"], null);

    /// <summary>The command that shows one interface: <c>wg show &lt;name&gt;</c>.</summary>
    /// <param name="name">The interface name.</param>
    /// <returns>The command.</returns>
    public static WireGuardCommand ShowInterface(string name) => new(WireGuardExecutable, ["show", name], null);

    /// <summary>The command that generates a private key: <c>wg genkey</c>.</summary>
    /// <returns>The command.</returns>
    public static WireGuardCommand GeneratePrivateKey() => new(WireGuardExecutable, ["genkey"], null);

    /// <summary>The command that generates a preshared key: <c>wg genpsk</c>.</summary>
    /// <returns>The command.</returns>
    public static WireGuardCommand GeneratePresharedKey() => new(WireGuardExecutable, ["genpsk"], null);

    /// <summary>
    /// The command that derives a public key: <c>wg pubkey</c> with the private
    /// key on standard input, which is where <c>wg</c> reads it.
    /// </summary>
    /// <param name="privateKey">The private key to derive from.</param>
    /// <returns>The command.</returns>
    public static WireGuardCommand DerivePublicKey(SecretKey privateKey)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        return new WireGuardCommand(WireGuardExecutable, ["pubkey"], privateKey.Reveal());
    }

    /// <summary>Renders the command line without key material.</summary>
    /// <returns>The executable and its arguments.</returns>
    public override string ToString() => Arguments.Count == 0
        ? Executable
        : string.Concat(Executable, " ", string.Join(' ', Arguments));
}
