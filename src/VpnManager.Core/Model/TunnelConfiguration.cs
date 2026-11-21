namespace VpnManager.Core.Model;

/// <summary>
/// A complete tunnel configuration: the interface and its peers. The tunnel
/// name is the configuration file stem, which is what <c>wg-quick</c> resolves.
/// </summary>
public sealed record TunnelConfiguration
{
    /// <summary>Gets the tunnel name (the configuration file stem).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the interface section.</summary>
    public InterfaceSettings Interface { get; init; } = new();

    /// <summary>Gets the peers in file order.</summary>
    public IReadOnlyList<PeerConfiguration> Peers { get; init; } = [];
}
