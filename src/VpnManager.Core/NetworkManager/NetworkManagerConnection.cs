namespace VpnManager.Core.NetworkManager;

/// <summary>
/// One NetworkManager WireGuard connection: the profile the workstation product
/// imports with <c>nmcli connection import</c>. NetworkManager owns the tunnel,
/// so the application lists it and activates it through NetworkManager rather
/// than through <c>wg-quick</c>.
/// </summary>
/// <param name="Name">The connection id, for example <c>work-vpn</c>.</param>
/// <param name="InterfaceName">The interface the connection creates, for example <c>wg0</c>.</param>
/// <param name="IsActive">Whether NetworkManager reports it currently active.</param>
/// <param name="Permissions">The <c>connection.permissions</c> entries, for example <c>user:alice</c>.</param>
public sealed record NetworkManagerConnection(
    string Name,
    string InterfaceName,
    bool IsActive,
    IReadOnlyList<string> Permissions)
{
    /// <summary>Decides whether a named account is allowed to activate the connection.</summary>
    /// <param name="userName">The account name, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the connection lists <c>user:&lt;userName&gt;</c>.</returns>
    public bool IsOwnedBy(string? userName) =>
        !string.IsNullOrWhiteSpace(userName)
        && Permissions.Contains(string.Concat("user:", userName), StringComparer.Ordinal);
}
