using System.Globalization;
using System.Text;

namespace VpnManager.Core.Logging;

/// <summary>
/// A rotating text-file logger. It writes one line per entry, rolls the file to
/// <c>&lt;name&gt;.1</c> once it passes a size limit and keeps a bounded number of
/// archives. It uses no reflection and no configuration binding, so it is safe
/// under Native AOT, and it never throws: a logger must not take the application
/// down. The file and its directory are owner-only.
/// </summary>
public sealed class RotatingFileLogger : IApplicationLogger, IDisposable
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode OwnerOnlyDirectory =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _directory;
    private readonly string _path;
    private readonly long _maximumBytes;
    private readonly int _retainedFiles;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();
    private bool _disposed;

    /// <summary>Initializes the logger.</summary>
    /// <param name="directory">The directory that holds the log files.</param>
    /// <param name="fileName">The current log file name.</param>
    /// <param name="maximumBytes">The size at which the file rotates.</param>
    /// <param name="retainedFiles">How many rotated archives to keep.</param>
    /// <param name="timeProvider">The clock, for deterministic tests.</param>
    public RotatingFileLogger(
        string directory,
        string fileName,
        long maximumBytes = 1024 * 1024,
        int retainedFiles = 5,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(retainedFiles, 1);

        _directory = directory;
        _path = Path.Combine(directory, fileName);
        _maximumBytes = maximumBytes;
        _retainedFiles = retainedFiles;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public void Log(LogLevel level, string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                EnsureDirectory();
                Rotate();
                Append(level, message);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Logging is best effort; a full or read-only disk is not fatal.
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }
    }

    private void EnsureDirectory()
    {
        Directory.CreateDirectory(_directory);
        File.SetUnixFileMode(_directory, OwnerOnlyDirectory);
    }

    private void Rotate()
    {
        var info = new FileInfo(_path);
        if (!info.Exists || info.Length < _maximumBytes)
        {
            return;
        }

        var oldest = ArchivePath(_retainedFiles);
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (var index = _retainedFiles - 1; index >= 1; index--)
        {
            var source = ArchivePath(index);
            if (File.Exists(source))
            {
                File.Move(source, ArchivePath(index + 1), overwrite: true);
            }
        }

        File.Move(_path, ArchivePath(1), overwrite: true);
    }

    private void Append(LogLevel level, string message)
    {
        if (!File.Exists(_path))
        {
            File.WriteAllText(_path, string.Empty, Utf8);
            File.SetUnixFileMode(_path, OwnerOnly);
        }

        var timestamp = _timeProvider.GetUtcNow().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        File.AppendAllText(
            _path,
            string.Create(CultureInfo.InvariantCulture, $"{timestamp} [{level}] {message}{Environment.NewLine}"),
            Utf8);
    }

    private string ArchivePath(int index) => string.Concat(_path, ".", index.ToString(CultureInfo.InvariantCulture));
}
