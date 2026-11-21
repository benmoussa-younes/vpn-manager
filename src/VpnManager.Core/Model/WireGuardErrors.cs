using Core.Results;

namespace VpnManager.Core.Model;

/// <summary>
/// Constructs the <see cref="CoreError"/> values the application returns. One
/// place for the wording keeps the messages consistent and testable.
/// </summary>
/// <remarks>
/// No factory ever receives or echoes a private key or a preshared key. Only
/// public keys may appear in a message.
/// </remarks>
public static class WireGuardErrors
{
    /// <summary>Creates a validation failure.</summary>
    /// <param name="code">One of the <c>validation.*</c> codes.</param>
    /// <param name="message">The actionable message shown to the operator.</param>
    /// <returns>The error.</returns>
    public static CoreError Validation(string code, string message) => new(code, message);

    /// <summary>Creates a configuration-store failure.</summary>
    /// <param name="code">One of the <c>config.*</c> codes.</param>
    /// <param name="message">The message.</param>
    /// <returns>The error.</returns>
    public static CoreError Configuration(string code, string message) => new(code, message);

    /// <summary>Creates the "no such configuration" error.</summary>
    /// <param name="name">The tunnel name.</param>
    /// <returns>The error.</returns>
    public static CoreError ConfigNotFound(string name) => new(
        WireGuardErrorCodes.ConfigNotFound,
        $"No configuration file exists for tunnel '{name}'.");

    /// <summary>Creates the "configuration already exists" error.</summary>
    /// <param name="name">The tunnel name.</param>
    /// <returns>The error.</returns>
    public static CoreError ConfigExists(string name) => new(
        WireGuardErrorCodes.ConfigExists,
        $"A configuration file already exists for tunnel '{name}'.");

    /// <summary>Creates the permission failure returned when the file system refuses the operation.</summary>
    /// <param name="path">The path that was refused.</param>
    /// <param name="message">The underlying message.</param>
    /// <returns>The error.</returns>
    public static CoreError PermissionDenied(string path, string message) => new(
        WireGuardErrorCodes.ConfigPermissionDenied,
        $"Permission denied for '{path}'. The account does not have read or write access to it. ({message})");

    /// <summary>Creates a command failure.</summary>
    /// <param name="command">The command line that failed, without secrets.</param>
    /// <param name="exitCode">The process exit code.</param>
    /// <param name="standardError">The captured standard error.</param>
    /// <returns>The error.</returns>
    public static CoreError CommandFailed(string command, int exitCode, string standardError)
    {
        var detail = string.IsNullOrWhiteSpace(standardError) ? "no standard error was produced" : standardError.Trim();
        return new CoreError(
            WireGuardErrorCodes.CommandFailed,
            $"'{command}' exited with code {exitCode}: {detail}");
    }

    /// <summary>Creates the failure returned when a command needs privileges this process does not have.</summary>
    /// <param name="command">The command line that was refused, without secrets.</param>
    /// <param name="standardError">The captured standard error.</param>
    /// <returns>The error.</returns>
    public static CoreError CommandPermissionDenied(string command, string standardError)
    {
        var detail = string.IsNullOrWhiteSpace(standardError) ? "the operating system refused the operation" : standardError.Trim();
        return new CoreError(
            WireGuardErrorCodes.PermissionDenied,
            $"'{command}' needs privileges this process does not have: {detail}");
    }

    /// <summary>Creates the failure returned when the WireGuard tools are not installed.</summary>
    /// <param name="executable">The executable that could not be started.</param>
    /// <returns>The error.</returns>
    public static CoreError ToolsUnavailable(string executable) => new(
        WireGuardErrorCodes.ToolsUnavailable,
        $"The '{executable}' executable could not be started. Install wireguard-tools and make sure it is on PATH.");

    /// <summary>Creates the failure returned when an interface does not exist.</summary>
    /// <param name="name">The interface name.</param>
    /// <returns>The error.</returns>
    public static CoreError InterfaceNotFound(string name) => new(
        WireGuardErrorCodes.InterfaceNotFound,
        $"Interface '{name}' does not exist.");

    /// <summary>Creates a key-generation failure.</summary>
    /// <param name="message">The underlying message.</param>
    /// <returns>The error.</returns>
    public static CoreError KeyGenerationFailed(string message) => new(
        WireGuardErrorCodes.KeyGenerationFailed,
        $"Key generation failed: {message}");

    /// <summary>Creates the failure returned when NetworkManager is absent.</summary>
    /// <returns>The error.</returns>
    public static CoreError NetworkManagerUnavailable() => new(
        WireGuardErrorCodes.NetworkManagerUnavailable,
        "NetworkManager is not available on this machine; its WireGuard connections cannot be read or changed.");

    /// <summary>Creates a failure for an <c>nmcli</c> command that exited non-zero.</summary>
    /// <param name="command">The command, without secrets.</param>
    /// <param name="exitCode">The process exit code.</param>
    /// <param name="standardError">The captured standard error.</param>
    /// <returns>The error.</returns>
    public static CoreError NetworkManagerFailed(string command, int exitCode, string standardError)
    {
        var detail = string.IsNullOrWhiteSpace(standardError) ? "no standard error was produced" : standardError.Trim();
        return new CoreError(
            WireGuardErrorCodes.NetworkManagerFailed,
            $"'{command}' exited with code {exitCode}: {detail}");
    }

    /// <summary>Creates the failure returned when a connection name is not a safe argument.</summary>
    /// <param name="name">The connection id.</param>
    /// <returns>The error.</returns>
    public static CoreError NetworkManagerInvalidConnection(string name) => new(
        WireGuardErrorCodes.NetworkManagerInvalidConnection,
        $"NetworkManager connection '{name}' is not a safe command argument.");

    /// <summary>Creates the failure returned when a path would be read as a command option.</summary>
    /// <param name="path">The refused path.</param>
    /// <returns>The error.</returns>
    public static CoreError NetworkManagerInvalidPath(string path) => new(
        WireGuardErrorCodes.NetworkManagerInvalidPath,
        $"The path '{path}' cannot be handed to NetworkManager: it starts with '-', which would be read as an option.");

    /// <summary>Creates the failure returned when an autoconnect priority is invalid.</summary>
    /// <param name="priority">The rejected priority.</param>
    /// <returns>The error.</returns>
    public static CoreError NetworkManagerInvalidPriority(int priority) => new(
        WireGuardErrorCodes.NetworkManagerInvalidPriority,
        $"NetworkManager autoconnect priority {priority} is outside -999 to 999.");

    /// <summary>Creates the failure returned when a tunnel is a NetworkManager connection, not a file.</summary>
    /// <param name="name">The connection id.</param>
    /// <returns>The error.</returns>
    public static CoreError NetworkManagerManaged(string name) => new(
        WireGuardErrorCodes.NetworkManagerManaged,
        $"Tunnel '{name}' is a NetworkManager connection; it is provisioned with the workstation and is not a wg-quick file.");

    /// <summary>Creates the failure returned when a read-only session tries to change something.</summary>
    /// <param name="operation">The refused operation.</param>
    /// <returns>The error.</returns>
    public static CoreError ReadOnly(string operation) => new(
        WireGuardErrorCodes.ReadOnly,
        $"The session is read-only: '{operation}' is not allowed.");

    /// <summary>Creates the failure returned when another tunnel is already connected.</summary>
    /// <param name="active">The tunnel that is already connected.</param>
    /// <param name="requested">The tunnel the operator tried to connect.</param>
    /// <returns>The error.</returns>
    public static CoreError TunnelAlreadyConnected(string active, string requested) => new(
        WireGuardErrorCodes.TunnelAlreadyConnected,
        $"Tunnel '{active}' is already connected. Disconnect it before connecting '{requested}'; only one tunnel can be connected at a time.");

    /// <summary>Creates the failure returned when the application has no state store.</summary>
    /// <returns>The error.</returns>
    public static CoreError StateUnavailable() => new(
        WireGuardErrorCodes.StateUnavailable,
        "The application has no state store, so the change cannot be remembered.");

    /// <summary>Creates the failure returned when the resolver state cannot be read.</summary>
    /// <returns>The error.</returns>
    public static CoreError DnsUnavailable() => new(
        WireGuardErrorCodes.DnsUnavailable,
        "The resolver state is not available on this machine.");
}
