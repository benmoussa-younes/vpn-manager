using Core.Results;
using VpnManager.Core.Model;
using VpnManager.Core.Storage;

namespace VpnManager.Core.Tests.Fakes;

/// <summary>
/// An in-memory <see cref="IConfigFileSystem"/>. The denials are scripted, which
/// is how the root-only <c>/etc/wireguard</c> behaviour is tested without root.
/// </summary>
internal sealed class FakeConfigFileSystem : IConfigFileSystem
{
    /// <summary>Path to content.</summary>
    internal Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);

    /// <summary>Directories whose listing is refused.</summary>
    internal HashSet<string> DeniedDirectories { get; } = new(StringComparer.Ordinal);

    /// <summary>Files whose read or delete is refused.</summary>
    internal HashSet<string> DeniedFiles { get; } = new(StringComparer.Ordinal);

    /// <summary>Directories that do not exist.</summary>
    internal HashSet<string> MissingDirectories { get; } = new(StringComparer.Ordinal);

    /// <summary>Every write, in order.</summary>
    internal List<(string Path, string Contents)> Writes { get; } = [];

    /// <summary>The permission failure the fake produces.</summary>
    internal static CoreError Denied(string path) =>
        new(WireGuardErrorCodes.ConfigPermissionDenied, $"simulated denial for '{path}'");

    public bool FileExists(string path) => Files.ContainsKey(path);

    public bool DirectoryExists(string path) => !MissingDirectories.Contains(path);

    public bool CreateDirectory(string path)
    {
        if (DeniedDirectories.Contains(path))
        {
            return false;
        }

        MissingDirectories.Remove(path);
        return true;
    }

    public Result<IReadOnlyList<string>> ListFileNames(string directory)
    {
        if (DeniedDirectories.Contains(directory))
        {
            return Result<IReadOnlyList<string>>.Failure(Denied(directory));
        }

        var names = Files.Keys
            .Where(path => string.Equals(Path.GetDirectoryName(path), directory, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Select(name => name!)
            .ToArray();

        return Result<IReadOnlyList<string>>.Success(names);
    }

    public Result<string> ReadAllText(string path)
    {
        if (DeniedFiles.Contains(path))
        {
            return Result<string>.Failure(Denied(path));
        }

        return Files.TryGetValue(path, out var contents)
            ? Result<string>.Success(contents)
            : Result<string>.Failure(WireGuardErrors.Configuration(WireGuardErrorCodes.ConfigNotFound, $"'{path}' does not exist."));
    }

    public Result<string> WriteAllText(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        if (DeniedDirectories.Contains(directory) || DeniedFiles.Contains(path))
        {
            return Result<string>.Failure(Denied(path));
        }

        Files[path] = contents;
        Writes.Add((path, contents));
        return Result<string>.Success(path);
    }

    public Result<string> DeleteFile(string path)
    {
        if (DeniedFiles.Contains(path))
        {
            return Result<string>.Failure(Denied(path));
        }

        Files.Remove(path);
        return Result<string>.Success(path);
    }
}

/// <summary>
/// An in-memory <see cref="IWireGuardConfigStore"/> for the service and presenter
/// tests. It records what was written and can refuse by name.
/// </summary>
internal sealed class FakeWireGuardConfigStore : IWireGuardConfigStore
{
    internal FakeWireGuardConfigStore(string directory = "/etc/wireguard") => ConfigDirectory = directory;

    public string ConfigDirectory { get; }

    /// <summary>Whether reading a tunnel crosses an elevation boundary.</summary>

    /// <summary>Tunnel name to configuration text.</summary>
    internal Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);

    /// <summary>Names whose read is refused.</summary>
    internal HashSet<string> DeniedReads { get; } = new(StringComparer.Ordinal);

    /// <summary>Names whose write is refused.</summary>
    internal HashSet<string> DeniedWrites { get; } = new(StringComparer.Ordinal);

    /// <summary>External paths that exist, for import and export.</summary>
    internal Dictionary<string, string> External { get; } = new(StringComparer.Ordinal);

    /// <summary>External paths considered to already exist but unreadable.</summary>
    internal HashSet<string> ExistingExternal { get; } = new(StringComparer.Ordinal);

    /// <summary>When set, the listing fails with this error.</summary>
    internal CoreError? ListError { get; set; }

    /// <summary>When set, the existence check fails with this error.</summary>
    internal CoreError? ExistsError { get; set; }

    /// <summary>Every successful write, in order.</summary>
    internal List<(string Name, string Contents)> Writes { get; } = [];

    /// <summary>The number of times a configuration text was requested.</summary>
    internal int ReadCount { get; private set; }

    /// <summary>Every successful export, in order.</summary>
    internal List<(string Path, string Contents)> Exports { get; } = [];

    internal static CoreError Denied(string name) =>
        new(WireGuardErrorCodes.ConfigPermissionDenied, $"simulated denial for '{name}'");

    public string GetConfigPath(string name) => Path.Combine(ConfigDirectory, string.Concat(name, ".conf"));

    public Result<IReadOnlyList<string>> ListTunnelNames() => ListError is null
        ? Result<IReadOnlyList<string>>.Success(Files.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray())
        : Result<IReadOnlyList<string>>.Failure(ListError);

    public Result<bool> Exists(string name) => ExistsError is not null
        ? Result<bool>.Failure(ExistsError)
        : Result<bool>.Success(Files.ContainsKey(name));

    public Result<string> Read(string name)
    {
        ReadCount++;

        if (DeniedReads.Contains(name))
        {
            return Result<string>.Failure(Denied(name));
        }

        return Files.TryGetValue(name, out var contents)
            ? Result<string>.Success(contents)
            : Result<string>.Failure(WireGuardErrors.ConfigNotFound(name));
    }

    public Result<string> Write(string name, string configText)
    {
        if (DeniedWrites.Contains(name))
        {
            return Result<string>.Failure(Denied(name));
        }

        Files[name] = configText;
        Writes.Add((name, configText));
        return Result<string>.Success(GetConfigPath(name));
    }

    public Result<string> Delete(string name)
    {
        if (!Files.Remove(name))
        {
            return Result<string>.Failure(WireGuardErrors.ConfigNotFound(name));
        }

        return Result<string>.Success(GetConfigPath(name));
    }

    public Result<string> ReadFile(string path) => External.TryGetValue(path, out var contents)
        ? Result<string>.Success(contents)
        : Result<string>.Failure(WireGuardErrors.Configuration(WireGuardErrorCodes.ConfigReadFailed, $"'{path}' cannot be read."));

    public Result<bool> FileExists(string path) =>
        Result<bool>.Success(External.ContainsKey(path) || ExistingExternal.Contains(path));

    public Result<string> WriteFile(string path, string configText)
    {
        External[path] = configText;
        Exports.Add((path, configText));
        return Result<string>.Success(path);
    }
}
