using Core.Results;
using VpnManager.Core.Model;
using VpnManager.Core.Validation;

namespace VpnManager.Core.Storage;

/// <summary>
/// The configuration store over an injectable file system. Every method returns
/// a failure value rather than throwing, so an unreadable directory is a state
/// the application renders, not a crash.
/// </summary>
public sealed class FileSystemWireGuardConfigStore : IWireGuardConfigStore
{
    private const string ConfigurationExtension = ".conf";

    private readonly IConfigFileSystem _fileSystem;

    /// <summary>Initializes the store.</summary>
    /// <param name="configDirectory">The directory holding the tunnel configuration files.</param>
    /// <param name="fileSystem">The file-system seam.</param>
    public FileSystemWireGuardConfigStore(string configDirectory, IConfigFileSystem fileSystem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configDirectory);
        ArgumentNullException.ThrowIfNull(fileSystem);

        ConfigDirectory = configDirectory;
        _fileSystem = fileSystem;
    }

    /// <inheritdoc />
    public string ConfigDirectory { get; }

    /// <inheritdoc />
    public string GetConfigPath(string tunnelName)
    {
        if (!TunnelNameRules.IsValid(tunnelName))
        {
            throw new ArgumentException($"'{tunnelName}' is not a usable tunnel name.", nameof(tunnelName));
        }

        return Path.Combine(ConfigDirectory, string.Concat(tunnelName, ConfigurationExtension));
    }

    /// <inheritdoc />
    public Result<IReadOnlyList<string>> ListTunnelNames()
    {
        if (!_fileSystem.DirectoryExists(ConfigDirectory))
        {
            return Result<IReadOnlyList<string>>.Failure(WireGuardErrors.Configuration(
                WireGuardErrorCodes.ConfigListFailed,
                $"The configuration directory '{ConfigDirectory}' does not exist."));
        }

        var listing = _fileSystem.ListFileNames(ConfigDirectory);
        if (!listing.TryGetValue(out var fileNames))
        {
            return Result<IReadOnlyList<string>>.Failure(listing.Error);
        }

        var names = fileNames
            .Where(name => name.EndsWith(ConfigurationExtension, StringComparison.OrdinalIgnoreCase))
            .Select(name => name[..^ConfigurationExtension.Length])
            .Where(TunnelNameRules.IsValid)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        return Result<IReadOnlyList<string>>.Success(names);
    }

    /// <inheritdoc />
    public Result<bool> Exists(string tunnelName)
    {
        var path = TryGetPath(tunnelName, out var failure);
        return path is null
            ? Result<bool>.Failure(failure!)
            : Result<bool>.Success(_fileSystem.FileExists(path));
    }

    /// <inheritdoc />
    public Result<string> Read(string tunnelName)
    {
        var path = TryGetPath(tunnelName, out var failure);
        return path is null ? Result<string>.Failure(failure!) : _fileSystem.ReadAllText(path);
    }

    /// <inheritdoc />
    public Result<string> Write(string tunnelName, string configText)
    {
        ArgumentNullException.ThrowIfNull(configText);

        var path = TryGetPath(tunnelName, out var failure);
        if (path is null)
        {
            return Result<string>.Failure(failure!);
        }

        if (!_fileSystem.DirectoryExists(ConfigDirectory) && !_fileSystem.CreateDirectory(ConfigDirectory))
        {
            // A user-scope directory is the operator's own and is created on the
            // first write; a system one that cannot be created stays a value.
            return Result<string>.Failure(WireGuardErrors.Configuration(
                WireGuardErrorCodes.ConfigWriteFailed,
                $"The configuration directory '{ConfigDirectory}' could not be created."));
        }

        return _fileSystem.WriteAllText(path, configText);
    }

    /// <inheritdoc />
    public Result<string> Delete(string tunnelName)
    {
        var path = TryGetPath(tunnelName, out var failure);
        if (path is null)
        {
            return Result<string>.Failure(failure!);
        }

        return _fileSystem.FileExists(path)
            ? _fileSystem.DeleteFile(path)
            : Result<string>.Failure(WireGuardErrors.ConfigNotFound(tunnelName));
    }

    /// <inheritdoc />
    public Result<string> ReadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return _fileSystem.ReadAllText(path);
    }

    /// <inheritdoc />
    public Result<bool> FileExists(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Result<bool>.Success(_fileSystem.FileExists(path));
    }

    /// <inheritdoc />
    public Result<string> WriteFile(string path, string configText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(configText);
        return _fileSystem.WriteAllText(path, configText);
    }

    private string? TryGetPath(string tunnelName, out CoreError? failure)
    {
        if (TunnelNameRules.IsValid(tunnelName))
        {
            failure = null;
            return Path.Combine(ConfigDirectory, string.Concat(tunnelName, ConfigurationExtension));
        }

        failure = WireGuardErrors.Validation(
            WireGuardErrorCodes.TunnelNameInvalid,
            $"'{tunnelName}' is not a usable tunnel name.");
        return null;
    }
}
