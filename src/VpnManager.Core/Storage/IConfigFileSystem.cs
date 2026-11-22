using Core.Results;

namespace VpnManager.Core.Storage;

/// <summary>
/// The file-system seam the configuration store is built on. It exists so the
/// store's behaviour — including a permission refusal — is unit-tested with a
/// fake, and so the only real file access lives in one small adapter.
/// </summary>
public interface IConfigFileSystem
{
    /// <summary>Decides whether a regular file exists at the path.</summary>
    /// <param name="path">The absolute path.</param>
    /// <returns><see langword="true"/> when it exists.</returns>
    bool FileExists(string path);

    /// <summary>Decides whether a directory exists at the path.</summary>
    /// <param name="path">The absolute path.</param>
    /// <returns><see langword="true"/> when it exists.</returns>
    bool DirectoryExists(string path);

    /// <summary>Creates a directory, and its parents, when it does not exist.</summary>
    /// <param name="path">The absolute path.</param>
    /// <returns><see langword="true"/> when the directory exists afterwards.</returns>
    bool CreateDirectory(string path);

    /// <summary>Lists the file names (not paths) directly inside a directory.</summary>
    /// <param name="directory">The directory.</param>
    /// <returns>The names, or a failure when the directory cannot be read.</returns>
    Result<IReadOnlyList<string>> ListFileNames(string directory);

    /// <summary>Reads a text file.</summary>
    /// <param name="path">The absolute path.</param>
    /// <returns>The contents, or a failure.</returns>
    Result<string> ReadAllText(string path);

    /// <summary>Writes a text file atomically, creating it with owner-only permissions.</summary>
    /// <param name="path">The absolute path.</param>
    /// <param name="contents">The text.</param>
    /// <returns>The path on success, or a failure.</returns>
    Result<string> WriteAllText(string path, string contents);

    /// <summary>Deletes a file.</summary>
    /// <param name="path">The absolute path.</param>
    /// <returns>The path on success, or a failure.</returns>
    Result<string> DeleteFile(string path);
}
