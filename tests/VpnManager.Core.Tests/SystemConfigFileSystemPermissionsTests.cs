using System.Text;
using VpnManager.Core.Storage;
using VpnManager.Core.Tests.Fakes;

namespace VpnManager.Core.Tests;

/// <summary>
/// The real file-system adapter must keep configuration files, and the directory
/// that holds them, owner-only: a configuration file carries a private key.
/// </summary>
public sealed class SystemConfigFileSystemPermissionsTests
{
    [Fact]
    public void A_written_file_is_owner_only_even_when_it_existed_with_loose_bits()
    {
        var fileSystem = TestFileSystems.SystemConfig();
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "wg0.conf");

        File.WriteAllText(path, "old");
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        var result = fileSystem.WriteAllText(path, "[Interface]\nPrivateKey = secret\n");

        Assert.True(result.IsSuccess);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }

    [Fact]
    public void A_created_directory_is_owner_only()
    {
        var fileSystem = TestFileSystems.SystemConfig();
        using var parent = new TemporaryDirectory();
        var path = Path.Combine(parent.Path, "wireguard");

        Assert.True(fileSystem.CreateDirectory(path));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(path));
    }

    [Fact]
    public void Reading_a_symbolic_link_is_refused()
    {
        var fileSystem = TestFileSystems.SystemConfig();
        using var directory = new TemporaryDirectory();
        var target = Path.Combine(directory.Path, "real.conf");
        var link = Path.Combine(directory.Path, "link.conf");
        File.WriteAllText(target, "[Interface]\nPrivateKey = secret\n");
        File.CreateSymbolicLink(link, target);

        var result = fileSystem.ReadAllText(link);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void A_pseudo_file_read_is_capped()
    {
        using var oversized = new MemoryStream(new byte[SystemConfigFileSystem.MaximumConfigurationBytes + 1]);

        var result = SystemConfigFileSystem.ReadCapped(oversized, "/proc/self/status");

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void A_pseudo_file_within_the_cap_is_read()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Name:\tvpn-manager\n"));

        var result = SystemConfigFileSystem.ReadCapped(stream, "/proc/self/status");

        Assert.True(result.IsSuccess);
        Assert.Contains("Name:", result.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void A_real_pseudo_file_is_still_readable()
    {
        var fileSystem = TestFileSystems.SystemConfig();

        var result = fileSystem.ReadAllText("/proc/self/status");

        Assert.True(result.IsSuccess);
        Assert.Contains("Name:", result.Value, StringComparison.Ordinal);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = Directory.CreateTempSubdirectory("wgm-fs-").FullName;

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
