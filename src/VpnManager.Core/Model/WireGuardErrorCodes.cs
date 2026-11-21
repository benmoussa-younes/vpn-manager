namespace VpnManager.Core.Model;

/// <summary>
/// Stable machine-readable error codes. The code never changes once shipped;
/// the human-readable message may.
/// </summary>
public static class WireGuardErrorCodes
{
    /// <summary>The tunnel name is empty.</summary>
    public const string TunnelNameEmpty = "validation.tunnel_name.empty";

    /// <summary>The tunnel name is not a legal Linux interface name.</summary>
    public const string TunnelNameInvalid = "validation.tunnel_name.invalid";

    /// <summary>The display name is too long.</summary>
    public const string DisplayNameInvalid = "validation.display_name.invalid";

    /// <summary>The interface has no private key.</summary>
    public const string PrivateKeyMissing = "validation.private_key.missing";

    /// <summary>The interface private key is not a WireGuard key.</summary>
    public const string PrivateKeyInvalid = "validation.private_key.invalid";

    /// <summary>A peer public key is not a WireGuard key.</summary>
    public const string KeyInvalid = "validation.key.invalid";

    /// <summary>A peer preshared key is not a WireGuard key.</summary>
    public const string PresharedKeyInvalid = "validation.preshared_key.invalid";

    /// <summary>The interface has no address.</summary>
    public const string AddressMissing = "validation.address.missing";

    /// <summary>An address is not a CIDR prefix.</summary>
    public const string AddressInvalid = "validation.address.invalid";

    /// <summary>The listen port is outside 0..65535.</summary>
    public const string ListenPortInvalid = "validation.listen_port.invalid";

    /// <summary>The MTU is outside the accepted range.</summary>
    public const string MtuInvalid = "validation.mtu.invalid";

    /// <summary>A DNS entry is neither an address nor a host name.</summary>
    public const string DnsInvalid = "validation.dns.invalid";

    /// <summary>The routing-table selector is not auto, off or a number.</summary>
    public const string TableInvalid = "validation.table.invalid";

    /// <summary>The fwmark is not off, a number or a hexadecimal value.</summary>
    public const string FwMarkInvalid = "validation.fwmark.invalid";

    /// <summary>A split switch needs the mesh AllowedIPs to replace the default route.</summary>
    public const string RoutingModeSplitMissingAllowedIps = "validation.routing_mode.split_missing_allowed_ips";

    /// <summary>A peer has no public key.</summary>
    public const string PeerPublicKeyMissing = "validation.peer.public_key.missing";

    /// <summary>The same peer public key appears twice.</summary>
    public const string PeerPublicKeyDuplicate = "validation.peer.public_key.duplicate";

    /// <summary>A peer has no allowed IPs.</summary>
    public const string PeerAllowedIpsMissing = "validation.peer.allowed_ips.missing";

    /// <summary>An allowed-IPs entry is not a CIDR prefix.</summary>
    public const string AllowedIpsInvalid = "validation.allowed_ips.invalid";

    /// <summary>The endpoint is not <c>host:port</c>.</summary>
    public const string EndpointInvalid = "validation.endpoint.invalid";

    /// <summary>The keepalive interval is outside 0..65535.</summary>
    public const string KeepaliveInvalid = "validation.keepalive.invalid";

    /// <summary>No configuration file exists for the tunnel.</summary>
    public const string ConfigNotFound = "config.not_found";

    /// <summary>A configuration file already exists for the tunnel.</summary>
    public const string ConfigExists = "config.exists";

    /// <summary>The configuration text could not be parsed.</summary>
    public const string ConfigParseError = "config.parse_error";

    /// <summary>The configuration text uses a key the format does not define.</summary>
    public const string ConfigUnsupportedKey = "config.unsupported_key";

    /// <summary>The configuration could not be read.</summary>
    public const string ConfigReadFailed = "config.read_failed";

    /// <summary>The configuration could not be written.</summary>
    public const string ConfigWriteFailed = "config.write_failed";

    /// <summary>The configuration could not be deleted.</summary>
    public const string ConfigDeleteFailed = "config.delete_failed";

    /// <summary>The configuration directory could not be listed.</summary>
    public const string ConfigListFailed = "config.list_failed";

    /// <summary>The operation was refused by the file system permissions.</summary>
    public const string ConfigPermissionDenied = "config.permission_denied";

    /// <summary>The runtime counters of an interface could not be read.</summary>
    public const string CountersUnavailable = "runtime.counters_unavailable";

    /// <summary>The resolver state could not be read.</summary>
    public const string DnsUnavailable = "runtime.dns_unavailable";

    /// <summary>The WireGuard tools are not available on this machine.</summary>
    public const string ToolsUnavailable = "runtime.tools_unavailable";

    /// <summary>The external command exited non-zero.</summary>
    public const string CommandFailed = "runtime.command_failed";

    /// <summary>The external command was refused because it needs privileges.</summary>
    public const string PermissionDenied = "runtime.permission_denied";

    /// <summary>No such interface.</summary>
    public const string InterfaceNotFound = "runtime.interface_not_found";

    /// <summary>The command output could not be parsed.</summary>
    public const string ParseError = "runtime.parse_error";

    /// <summary>Key generation failed.</summary>
    public const string KeyGenerationFailed = "runtime.key_generation_failed";

    /// <summary>The tunnel is already up.</summary>
    public const string TunnelAlreadyUp = "tunnel.already_up";

    /// <summary>Another tunnel is already connected; only one tunnel may be up.</summary>
    public const string TunnelAlreadyConnected = "tunnel.already_connected";

    /// <summary>The tunnel is not up.</summary>
    public const string TunnelNotUp = "tunnel.not_up";

    /// <summary>A tunnel with that name already exists.</summary>
    public const string TunnelNameConflict = "tunnel.name_conflict";

    /// <summary>The imported text is not a usable configuration.</summary>
    public const string ImportInvalid = "import.invalid";

    /// <summary>The configuration could not be exported.</summary>
    public const string ExportFailed = "export.failed";

    /// <summary>NetworkManager is not available, so its connections cannot be read or changed.</summary>
    public const string NetworkManagerUnavailable = "network_manager.unavailable";

    /// <summary>An <c>nmcli</c> command exited non-zero.</summary>
    public const string NetworkManagerFailed = "network_manager.failed";

    /// <summary>A NetworkManager connection name is not a safe command argument.</summary>
    public const string NetworkManagerInvalidConnection = "network_manager.invalid_connection";

    /// <summary>A file path handed to NetworkManager is not a safe command argument.</summary>
    public const string NetworkManagerInvalidPath = "network_manager.invalid_path";

    /// <summary>A NetworkManager autoconnect priority is outside its accepted range.</summary>
    public const string NetworkManagerInvalidPriority = "network_manager.invalid_priority";

    /// <summary>The tunnel is owned by NetworkManager and is not a wg-quick file.</summary>
    public const string NetworkManagerManaged = "network_manager.managed";

    /// <summary>A read-only session refused a change.</summary>
    public const string ReadOnly = "read_only.refused";

    /// <summary>The application has no state store, so it cannot remember the change.</summary>
    public const string StateUnavailable = "state.unavailable";
}
