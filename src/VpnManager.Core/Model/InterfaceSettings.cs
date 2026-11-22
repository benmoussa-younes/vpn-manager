namespace VpnManager.Core.Model;

/// <summary>
/// The <c>[Interface]</c> section of a wg-quick configuration file. Repeatable
/// keys (<see cref="Addresses"/>, <see cref="Dns"/>, the hooks) keep every
/// occurrence and their original order.
/// </summary>
public sealed record InterfaceSettings
{
    /// <summary>Gets the interface private key. Empty when the section omits it.</summary>
    public SecretKey PrivateKey { get; init; } = SecretKey.Empty;

    /// <summary>Gets the interface addresses in CIDR notation, in file order.</summary>
    public IReadOnlyList<string> Addresses { get; init; } = [];

    /// <summary>Gets the resolver addresses, in file order.</summary>
    public IReadOnlyList<string> Dns { get; init; } = [];

    /// <summary>Gets the UDP listen port, or <see langword="null"/> when unset.</summary>
    public int? ListenPort { get; init; }

    /// <summary>Gets the interface MTU, or <see langword="null"/> when unset.</summary>
    public int? Mtu { get; init; }

    /// <summary>Gets the routing table selector (<c>auto</c>, <c>off</c> or a number).</summary>
    public string? Table { get; init; }

    /// <summary>Gets the firewall mark, or <see langword="null"/> when unset.</summary>
    public string? FwMark { get; init; }

    /// <summary>Gets whether wg-quick should save the runtime configuration on down.</summary>
    public bool? SaveConfig { get; init; }

    /// <summary>Gets the <c>PreUp</c> hook commands in file order.</summary>
    public IReadOnlyList<string> PreUp { get; init; } = [];

    /// <summary>Gets the <c>PostUp</c> hook commands in file order.</summary>
    public IReadOnlyList<string> PostUp { get; init; } = [];

    /// <summary>Gets the <c>PreDown</c> hook commands in file order.</summary>
    public IReadOnlyList<string> PreDown { get; init; } = [];

    /// <summary>Gets the <c>PostDown</c> hook commands in file order.</summary>
    public IReadOnlyList<string> PostDown { get; init; } = [];
}
