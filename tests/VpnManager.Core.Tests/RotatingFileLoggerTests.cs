using VpnManager.Core.Logging;

namespace VpnManager.Core.Tests;

/// <summary>The rotating file logger.</summary>
public sealed class RotatingFileLoggerTests
{
    [Fact]
    public void It_writes_one_line_per_entry()
    {
        using var directory = new TemporaryDirectory();
        using var logger = new RotatingFileLogger(directory.Path, "application.log");

        logger.Log(LogLevel.Information, "started");
        logger.Log(LogLevel.Error, "boom");

        var text = File.ReadAllText(Path.Combine(directory.Path, "application.log"));
        Assert.Contains("[Information] started", text, StringComparison.Ordinal);
        Assert.Contains("[Error] boom", text, StringComparison.Ordinal);
    }

    [Fact]
    public void It_rotates_and_keeps_the_retention()
    {
        using var directory = new TemporaryDirectory();
        using var logger = new RotatingFileLogger(directory.Path, "application.log", maximumBytes: 64, retainedFiles: 2);

        for (var index = 0; index < 20; index++)
        {
            logger.Log(LogLevel.Information, new string('x', 40));
        }

        Assert.True(File.Exists(Path.Combine(directory.Path, "application.log")));
        Assert.True(File.Exists(Path.Combine(directory.Path, "application.log.1")));
        Assert.True(File.Exists(Path.Combine(directory.Path, "application.log.2")));
        Assert.False(File.Exists(Path.Combine(directory.Path, "application.log.3")));
    }

    [Fact]
    public void The_log_file_is_owner_only()
    {
        using var directory = new TemporaryDirectory();
        using var logger = new RotatingFileLogger(directory.Path, "application.log");

        logger.Log(LogLevel.Information, "hello");

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(Path.Combine(directory.Path, "application.log")));
    }

    [Fact]
    public void A_disposed_logger_writes_nothing()
    {
        using var directory = new TemporaryDirectory();
        var logger = new RotatingFileLogger(directory.Path, "application.log");
        logger.Dispose();

        logger.Log(LogLevel.Information, "ignored");

        Assert.False(File.Exists(Path.Combine(directory.Path, "application.log")));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = Directory.CreateTempSubdirectory("wgm-log-").FullName;

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
                // Already gone.
            }
        }
    }
}
