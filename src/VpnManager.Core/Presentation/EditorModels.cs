using VpnManager.Core.Model;

namespace VpnManager.Core.Presentation;

/// <summary>
/// The editor's peer row, both as pushed to the view and as read back from it.
/// Every value is editor text; the presenter parses it.
/// </summary>
/// <param name="Label">The operator label, or empty.</param>
/// <param name="PublicKey">The peer public key.</param>
/// <param name="PresharedKey">The preshared key, or empty.</param>
/// <param name="AllowedIps">The allowed prefixes, one per line or comma separated.</param>
/// <param name="Endpoint">The endpoint, or empty.</param>
/// <param name="PersistentKeepalive">The keepalive interval in seconds, or empty.</param>
public sealed record EditorPeerInput(
    string Label,
    string PublicKey,
    string PresharedKey,
    string AllowedIps,
    string Endpoint,
    string PersistentKeepalive);

/// <summary>
/// Everything the view needs to render the editor. It never contains a private
/// key: <see cref="HasPrivateKey"/> says one is held by the presenter, and
/// <see cref="PublicKey"/> is the derived public key, which is safe to display.
/// </summary>
/// <param name="Title">The editor heading.</param>
/// <param name="IsNew">Whether this is a new tunnel rather than an edit.</param>
/// <param name="Name">The tunnel name text.</param>
/// <param name="NameEditable">Whether the name may be changed (false when editing).</param>
/// <param name="Addresses">The interface addresses, one per line.</param>
/// <param name="Dns">The resolver addresses, one per line.</param>
/// <param name="ListenPort">The listen port text, or empty.</param>
/// <param name="Mtu">The MTU text, or empty.</param>
/// <param name="Table">The routing table selector, or empty.</param>
/// <param name="RoutingMode">The routing mode the editor is set to.</param>
/// <param name="HasPrivateKey">Whether the presenter holds a private key for this tunnel.</param>
/// <param name="PublicKey">The derived interface public key, or <see langword="null"/>.</param>
/// <param name="Peers">The peer rows.</param>
public sealed record EditorSnapshot(
    string Title,
    bool IsNew,
    string Name,
    bool NameEditable,
    string Addresses,
    string Dns,
    string ListenPort,
    string Mtu,
    string Table,
    TunnelRoutingMode RoutingMode,
    bool HasPrivateKey,
    string? PublicKey,
    IReadOnlyList<EditorPeerInput> Peers);

/// <summary>
/// What the view read out of the editor when the operator asked to save. The
/// private key field is whatever was typed; empty means "keep the key the
/// presenter already holds".
/// </summary>
/// <param name="Name">The tunnel name text.</param>
/// <param name="PrivateKey">The typed private key, or empty.</param>
/// <param name="Addresses">The interface addresses text.</param>
/// <param name="Dns">The resolver addresses text.</param>
/// <param name="ListenPort">The listen port text.</param>
/// <param name="Mtu">The MTU text.</param>
/// <param name="Table">The routing table selector text.</param>
/// <param name="RoutingMode">The routing mode the operator selected.</param>
/// <param name="Peers">The peer rows.</param>
public sealed record EditorInput(
    string Name,
    string PrivateKey,
    string Addresses,
    string Dns,
    string ListenPort,
    string Mtu,
    string Table,
    TunnelRoutingMode RoutingMode,
    IReadOnlyList<EditorPeerInput> Peers);

/// <summary>
/// The editor field keys, so the view can mark the offending control and the
/// presenter maps every validation code onto one of them.
/// </summary>
public static class EditorField
{
    /// <summary>No specific field; shown as a general message.</summary>
    public const string General = "editor.general";

    /// <summary>The tunnel name field.</summary>
    public const string Name = "editor.name";

    /// <summary>The private key field.</summary>
    public const string PrivateKey = "editor.privateKey";

    /// <summary>The addresses field.</summary>
    public const string Addresses = "editor.addresses";

    /// <summary>The DNS field.</summary>
    public const string Dns = "editor.dns";

    /// <summary>The listen port field.</summary>
    public const string ListenPort = "editor.listenPort";

    /// <summary>The MTU field.</summary>
    public const string Mtu = "editor.mtu";

    /// <summary>The routing-table field.</summary>
    public const string Table = "editor.table";

    /// <summary>The routing-mode control.</summary>
    public const string RoutingMode = "editor.routingMode";

    /// <summary>The peer public key field.</summary>
    public const string PeerPublicKey = "editor.peer.publicKey";

    /// <summary>The peer preshared key field.</summary>
    public const string PeerPresharedKey = "editor.peer.presharedKey";

    /// <summary>The peer allowed IPs field.</summary>
    public const string PeerAllowedIps = "editor.peer.allowedIps";

    /// <summary>The peer endpoint field.</summary>
    public const string PeerEndpoint = "editor.peer.endpoint";

    /// <summary>The peer keepalive field.</summary>
    public const string PeerKeepalive = "editor.peer.keepalive";

    /// <summary>Maps a validation error code onto the field that caused it.</summary>
    /// <param name="errorCode">The <c>CoreError.Code</c>.</param>
    /// <returns>The field key.</returns>
    public static string FromErrorCode(string errorCode) => errorCode switch
    {
        Model.WireGuardErrorCodes.TunnelNameEmpty => Name,
        Model.WireGuardErrorCodes.TunnelNameInvalid => Name,
        Model.WireGuardErrorCodes.PrivateKeyMissing => PrivateKey,
        Model.WireGuardErrorCodes.PrivateKeyInvalid => PrivateKey,
        Model.WireGuardErrorCodes.AddressMissing => Addresses,
        Model.WireGuardErrorCodes.AddressInvalid => Addresses,
        Model.WireGuardErrorCodes.DnsInvalid => Dns,
        Model.WireGuardErrorCodes.ListenPortInvalid => ListenPort,
        Model.WireGuardErrorCodes.MtuInvalid => Mtu,
        Model.WireGuardErrorCodes.TableInvalid => Table,
        Model.WireGuardErrorCodes.FwMarkInvalid => Table,
        Model.WireGuardErrorCodes.RoutingModeSplitMissingAllowedIps => PeerAllowedIps,
        Model.WireGuardErrorCodes.PeerPublicKeyMissing => PeerPublicKey,
        Model.WireGuardErrorCodes.PeerPublicKeyDuplicate => PeerPublicKey,
        Model.WireGuardErrorCodes.KeyInvalid => PeerPublicKey,
        Model.WireGuardErrorCodes.PresharedKeyInvalid => PeerPresharedKey,
        Model.WireGuardErrorCodes.PeerAllowedIpsMissing => PeerAllowedIps,
        Model.WireGuardErrorCodes.AllowedIpsInvalid => PeerAllowedIps,
        Model.WireGuardErrorCodes.EndpointInvalid => PeerEndpoint,
        Model.WireGuardErrorCodes.KeepaliveInvalid => PeerKeepalive,
        _ => General,
    };
}
