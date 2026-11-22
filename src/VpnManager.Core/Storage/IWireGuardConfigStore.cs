using Core.Results;

namespace VpnManager.Core.Storage;

/// <summary>
/// The tunnel configuration files. It is the only component that knows where a
/// configuration lives; the application layer never builds a path or touches
/// <c>/etc/wireguard</c> itself.
/// </summary>
public interface IWireGuardConfigStore
{
    /// <summary>Gets the directory the store manages.</summary>
    string ConfigDirectory { get; }

    /// <summary>Builds the configuration path for a tunnel name.</summary>
    /// <param name="tunnelName">A name that already satisfies the tunnel-name rules.</param>
    /// <returns>The absolute path.</returns>
    /// <exception cref="ArgumentException">The name could not be a file stem.</exception>
    string GetConfigPath(string tunnelName);

    /// <summary>Lists the tunnels with a configuration file.</summary>
    /// <returns>The names, sorted, or a failure when the directory cannot be read.</returns>
    Result<IReadOnlyList<string>> ListTunnelNames();

    /// <summary>Decides whether a configuration exists.</summary>
    /// <param name="tunnelName">The tunnel name.</param>
    /// <returns>The answer, or a failure for an unusable name.</returns>
    Result<bool> Exists(string tunnelName);

    /// <summary>Reads a configuration file.</summary>
    /// <param name="tunnelName">The tunnel name.</param>
    /// <returns>The text, or a failure.</returns>
    Result<string> Read(string tunnelName);

    /// <summary>Writes a configuration file atomically.</summary>
    /// <param name="tunnelName">The tunnel name.</param>
    /// <param name="configText">The text to write.</param>
    /// <returns>The path on success, or a failure.</returns>
    Result<string> Write(string tunnelName, string configText);

    /// <summary>Deletes a configuration file.</summary>
    /// <param name="tunnelName">The tunnel name.</param>
    /// <returns>The path on success, or a failure.</returns>
    Result<string> Delete(string tunnelName);

    /// <summary>Reads a configuration file from an arbitrary path (import).</summary>
    /// <param name="path">The source path.</param>
    /// <returns>The text, or a failure.</returns>
    Result<string> ReadFile(string path);

    /// <summary>Decides whether a file exists at an arbitrary path (export destination).</summary>
    /// <param name="path">The path.</param>
    /// <returns>The answer.</returns>
    Result<bool> FileExists(string path);

    /// <summary>Writes a configuration file to an arbitrary path (export).</summary>
    /// <param name="path">The destination path.</param>
    /// <param name="configText">The text to write.</param>
    /// <returns>The path on success, or a failure.</returns>
    Result<string> WriteFile(string path, string configText);
}
