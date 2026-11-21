using System.Text;
using Core.IO.Files;
using Core.Platform;
using Core.Results;
using VpnManager.Core.Model;

namespace VpnManager.Core.Storage;

/// <summary>
/// The real file-system adapter. A profile is read with a bounded, no-follow
/// read and written through the platform atomic writer, so it is never observed
/// half-written and a new file is owner-only.
/// </summary>
public sealed class SystemConfigFileSystem : IConfigFileSystem
{
    /// <summary>The largest profile the store will read.</summary>
    public const long MaximumConfigurationBytes = 1_048_576;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly IAtomicFileWriterFactory _atomicFiles;
    private readonly IFileSystem _fileSystem;

    /// <summary>Initializes the adapter over the platform ports.</summary>
    /// <param name="atomicFiles">The atomic writer factory.</param>
    /// <param name="fileSystem">The file-system port used for a pinned no-follow read.</param>
    public SystemConfigFileSystem(IAtomicFileWriterFactory atomicFiles, IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(atomicFiles);
        ArgumentNullException.ThrowIfNull(fileSystem);

        _atomicFiles = atomicFiles;
        _fileSystem = fileSystem;
    }

    /// <inheritdoc />
    public bool FileExists(string path) => File.Exists(path);

    /// <inheritdoc />
    public bool DirectoryExists(string path) => Directory.Exists(path);

    /// <inheritdoc />
    public bool CreateDirectory(string path)
    {
        try
        {
            _atomicFiles.EnsureDirectory(path, ownerOnly: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public Result<IReadOnlyList<string>> ListFileNames(string directory)
    {
        try
        {
            var names = Directory.EnumerateFiles(directory, "*.conf", SearchOption.TopDirectoryOnly)
                .Select(path => Path.GetFileName(path))
                .ToArray();
            return Result<IReadOnlyList<string>>.Success(names);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result<IReadOnlyList<string>>.Failure(Map(directory, WireGuardErrorCodes.ConfigListFailed, exception));
        }
    }

    /// <inheritdoc />
    public Result<string> ReadAllText(string path)
    {
        try
        {
            // /proc and /sys report a zero size for a readable pseudo-file; the
            // bounded descriptor read is for ordinary profile files only.
            if (IsPseudoFile(path))
            {
                return Result<string>.Success(File.ReadAllText(path));
            }

            if (!_fileSystem.TryStat(path, out var status))
            {
                return Result<string>.Failure(WireGuardErrors.Configuration(
                    WireGuardErrorCodes.ConfigNotFound,
                    $"The file '{path}' does not exist."));
            }

            if (status.Type != FileType.RegularFile)
            {
                return Result<string>.Failure(WireGuardErrors.Configuration(
                    WireGuardErrorCodes.ConfigReadFailed,
                    $"The file '{path}' is not a regular file; refusing to read it."));
            }

            if (status.Size < 0 || status.Size > MaximumConfigurationBytes)
            {
                return Result<string>.Failure(WireGuardErrors.Configuration(
                    WireGuardErrorCodes.ConfigReadFailed,
                    $"The file '{path}' is larger than {MaximumConfigurationBytes} bytes; refusing to read it."));
            }

            if (!_fileSystem.TryAccess(path, FileAccessMode.Read))
            {
                return Result<string>.Failure(WireGuardErrors.PermissionDenied(
                    path,
                    "The file is not readable by this account."));
            }

            var opened = FileReader.OpenNoFollow(_fileSystem, path);
            if (opened.TryGetError(out var openError))
            {
                return Result<string>.Failure(WireGuardErrors.Configuration(
                    WireGuardErrorCodes.ConfigReadFailed,
                    $"The file '{path}' could not be read: {openError.Message}"));
            }

            using var reader = opened.Value;
            var bytes = new byte[(int)reader.Metadata.Size];
            var offset = 0;

            while (offset < bytes.Length)
            {
                var read = RandomAccess.Read(reader.Handle, bytes.AsSpan(offset), offset);
                if (read <= 0)
                {
                    return Result<string>.Failure(WireGuardErrors.Configuration(
                        WireGuardErrorCodes.ConfigReadFailed,
                        $"The file '{path}' ended before its declared size."));
                }

                offset += read;
            }

            return Result<string>.Success(StrictUtf8.GetString(bytes));
        }
        catch (DecoderFallbackException)
        {
            return Result<string>.Failure(WireGuardErrors.Configuration(
                WireGuardErrorCodes.ConfigReadFailed,
                $"The file '{path}' is not valid UTF-8."));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result<string>.Failure(Map(path, WireGuardErrorCodes.ConfigReadFailed, exception));
        }
    }

    /// <inheritdoc />
    public Result<string> WriteAllText(string path, string contents)
    {
        ArgumentNullException.ThrowIfNull(contents);

        try
        {
            if (Path.GetDirectoryName(path) is { Length: > 0 } directory)
            {
                if (Directory.Exists(directory) && !_fileSystem.TryAccess(directory, FileAccessMode.Write))
                {
                    return Result<string>.Failure(WireGuardErrors.PermissionDenied(
                        path,
                        "The directory is not writable by this account."));
                }

                _atomicFiles.EnsureDirectory(directory, ownerOnly: true);
            }

            var created = _atomicFiles.Create(path, new AtomicFileOptions { OwnerOnly = true });
            if (created.TryGetError(out var createError))
            {
                return Result<string>.Failure(Map(path, WireGuardErrorCodes.ConfigWriteFailed, createError));
            }

            using var writer = created.Value;
            using (var streamWriter = new StreamWriter(
                writer.Stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                leaveOpen: true))
            {
                streamWriter.Write(contents);
            }

            var commit = writer.Commit();
            return commit.TryGetError(out var commitError)
                ? Result<string>.Failure(Map(path, WireGuardErrorCodes.ConfigWriteFailed, commitError))
                : Result<string>.Success(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Result<string>.Failure(Map(path, WireGuardErrorCodes.ConfigWriteFailed, exception));
        }
    }

    /// <inheritdoc />
    public Result<string> DeleteFile(string path)
    {
        try
        {
            File.Delete(path);
            return Result<string>.Success(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result<string>.Failure(Map(path, WireGuardErrorCodes.ConfigDeleteFailed, exception));
        }
    }

    private static bool IsPseudoFile(string path) =>
        path.StartsWith("/proc/", StringComparison.Ordinal) ||
        path.StartsWith("/sys/", StringComparison.Ordinal);

    private static CoreError Map(string path, string failureCode, Exception exception) =>
        exception is UnauthorizedAccessException
            ? WireGuardErrors.PermissionDenied(path, exception.Message)
            : WireGuardErrors.Configuration(failureCode, $"'{path}': {exception.Message}");

    private static CoreError Map(string path, string failureCode, CoreError error) =>
        WireGuardErrors.Configuration(failureCode, $"'{path}': {error.Message}");
}
