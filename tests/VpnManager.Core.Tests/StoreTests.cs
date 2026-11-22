using VpnManager.Core.Model;
using VpnManager.Core.Storage;
using VpnManager.Core.Tests.Fakes;

namespace VpnManager.Core.Tests;

/// <summary>
/// The configuration store over the injectable file seam: every outcome,
/// including the permission refusal of a root-only directory, is a value.
/// </summary>
public sealed class FileSystemWireGuardConfigStoreTests
{
    private const string Directory = "/etc/wireguard";

    [Fact]
    public void The_path_is_the_directory_plus_the_name_and_extension()
    {
        var store = CreateStore(out _);

        Assert.Equal(Path.Combine(Directory, "wg0.conf"), store.GetConfigPath("wg0"));
    }

    [Theory]
    [InlineData("../etc/passwd")]
    [InlineData("a/b")]
    [InlineData("")]
    [InlineData("a\\b")]
    public void A_name_that_could_escape_the_directory_is_a_programming_error(string name)
    {
        var store = CreateStore(out _);

        Assert.Throws<ArgumentException>(() => store.GetConfigPath(name));
    }

    [Fact]
    public void Listing_returns_the_stems_of_the_configuration_files_only()
    {
        var store = CreateStore(out var fileSystem);
        fileSystem.Files[Path.Combine(Directory, "wg1.conf")] = string.Empty;
        fileSystem.Files[Path.Combine(Directory, "wg0.conf")] = string.Empty;
        fileSystem.Files[Path.Combine(Directory, "notes.txt")] = string.Empty;
        fileSystem.Files[Path.Combine(Directory, "bad name.conf")] = string.Empty;

        var result = store.ListTunnelNames();

        Assert.True(result.IsSuccess);
        Assert.Equal(["wg0", "wg1"], result.Value);
    }

    [Fact]
    public void Listing_a_missing_directory_is_refused()
    {
        var store = CreateStore(out var fileSystem);
        fileSystem.MissingDirectories.Add(Directory);

        var result = store.ListTunnelNames();

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.ConfigListFailed, result.Error.Code);
    }

    [Fact]
    public void Listing_a_directory_without_permission_is_refused_as_a_value()
    {
        var store = CreateStore(out var fileSystem);
        fileSystem.DeniedDirectories.Add(Directory);

        var result = store.ListTunnelNames();

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.ConfigPermissionDenied, result.Error.Code);
    }

    [Fact]
    public void Reading_returns_the_stored_text()
    {
        var store = CreateStore(out var fileSystem);
        fileSystem.Files[Path.Combine(Directory, "wg0.conf")] = TestData.SampleConfigText;

        var result = store.Read("wg0");

        Assert.True(result.IsSuccess);
        Assert.Equal(TestData.SampleConfigText, result.Value);
    }

    [Fact]
    public void Reading_a_missing_file_is_refused()
    {
        var store = CreateStore(out _);

        var result = store.Read("wg0");

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.ConfigNotFound, result.Error.Code);
    }

    [Fact]
    public void Reading_a_file_without_permission_is_refused_as_a_value()
    {
        var store = CreateStore(out var fileSystem);
        fileSystem.Files[Path.Combine(Directory, "wg0.conf")] = "content";
        fileSystem.DeniedFiles.Add(Path.Combine(Directory, "wg0.conf"));

        var result = store.Read("wg0");

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.ConfigPermissionDenied, result.Error.Code);
    }

    [Fact]
    public void Writing_stores_the_text_at_the_expected_path()
    {
        var store = CreateStore(out var fileSystem);

        var result = store.Write("wg0", TestData.SampleConfigText);

        Assert.True(result.IsSuccess);
        Assert.Equal(Path.Combine(Directory, "wg0.conf"), result.Value);
        var write = Assert.Single(fileSystem.Writes);
        Assert.Equal(TestData.SampleConfigText, write.Contents);
    }

    [Fact]
    public void Writing_into_a_missing_directory_creates_it()
    {
        var store = CreateStore(out var fileSystem);
        fileSystem.MissingDirectories.Add(Directory);

        var result = store.Write("wg0", "content");

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(Directory, fileSystem.MissingDirectories);
        var write = Assert.Single(fileSystem.Writes);
        Assert.Equal(Path.Combine(Directory, "wg0.conf"), write.Path);
    }

    [Fact]
    public void Writing_when_the_directory_cannot_be_created_is_refused_without_a_write()
    {
        var store = CreateStore(out var fileSystem);
        fileSystem.MissingDirectories.Add(Directory);
        fileSystem.DeniedDirectories.Add(Directory);

        var result = store.Write("wg0", "content");

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.ConfigWriteFailed, result.Error.Code);
        Assert.Empty(fileSystem.Writes);
    }

    [Fact]
    public void Writing_without_permission_is_refused_as_a_value()
    {
        var store = CreateStore(out var fileSystem);
        fileSystem.DeniedDirectories.Add(Directory);

        var result = store.Write("wg0", "content");

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.ConfigPermissionDenied, result.Error.Code);
        Assert.Empty(fileSystem.Writes);
    }

    [Fact]
    public void Writing_with_an_unusable_name_is_refused_without_touching_the_file_system()
    {
        var store = CreateStore(out var fileSystem);

        var result = store.Write("../escape", "content");

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.TunnelNameInvalid, result.Error.Code);
        Assert.Empty(fileSystem.Writes);
    }

    [Fact]
    public void Deleting_removes_the_file()
    {
        var store = CreateStore(out var fileSystem);
        fileSystem.Files[Path.Combine(Directory, "wg0.conf")] = "content";

        var result = store.Delete("wg0");

        Assert.True(result.IsSuccess);
        Assert.False(fileSystem.FileExists(Path.Combine(Directory, "wg0.conf")));
    }

    [Fact]
    public void Deleting_a_tunnel_without_a_file_is_refused()
    {
        var store = CreateStore(out _);

        var result = store.Delete("wg0");

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.ConfigNotFound, result.Error.Code);
    }

    [Fact]
    public void Deleting_with_an_unusable_name_is_refused()
    {
        var store = CreateStore(out _);

        var result = store.Delete("a/b");

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.TunnelNameInvalid, result.Error.Code);
    }

    [Fact]
    public void Existence_and_external_paths_are_reported()
    {
        var store = CreateStore(out var fileSystem);
        fileSystem.Files[Path.Combine(Directory, "wg0.conf")] = "content";
        fileSystem.Files["/tmp/import.conf"] = "imported";

        Assert.True(store.Exists("wg0").Value);
        Assert.False(store.Exists("wg9").Value);
        Assert.True(store.FileExists("/tmp/import.conf").Value);
        Assert.False(store.FileExists("/tmp/missing.conf").Value);
        Assert.Equal("imported", store.ReadFile("/tmp/import.conf").Value);
        Assert.True(store.ReadFile("/tmp/missing.conf").IsFailure);
        Assert.True(store.WriteFile("/tmp/export.conf", "exported").IsSuccess);
        Assert.Equal("exported", fileSystem.Files["/tmp/export.conf"]);
    }

    private static FileSystemWireGuardConfigStore CreateStore(out FakeConfigFileSystem fileSystem)
    {
        fileSystem = new FakeConfigFileSystem();
        return new FileSystemWireGuardConfigStore(Directory, fileSystem);
    }
}

/// <summary>
/// The real file-system adapter, against a disposable directory so nothing
/// outside the repository is written.
/// </summary>
public sealed class SystemConfigFileSystemTests
{
    [Fact]
    public void A_written_file_round_trips_and_keeps_owner_only_permissions()
    {
        using var directory = new TemporaryDirectory();
        var fileSystem = TestFileSystems.SystemConfig();
        var path = Path.Combine(directory.Path, "wg0.conf");

        var written = fileSystem.WriteAllText(path, TestData.SampleConfigText);

        Assert.True(written.IsSuccess, written.IsFailure ? written.Error.Message : string.Empty);
        Assert.True(fileSystem.FileExists(path));
        Assert.Equal(TestData.SampleConfigText, fileSystem.ReadAllText(path).Value);

        var mode = File.GetUnixFileMode(path);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
        Assert.False(mode.HasFlag(UnixFileMode.GroupRead));
        Assert.False(mode.HasFlag(UnixFileMode.OtherRead));
        Assert.False(mode.HasFlag(UnixFileMode.OtherExecute));
    }

    [Fact]
    public void A_second_write_replaces_the_content_and_keeps_the_mode()
    {
        using var directory = new TemporaryDirectory();
        var fileSystem = TestFileSystems.SystemConfig();
        var path = Path.Combine(directory.Path, "wg0.conf");

        fileSystem.WriteAllText(path, "first");
        var second = fileSystem.WriteAllText(path, "second");

        Assert.True(second.IsSuccess);
        Assert.Equal("second", fileSystem.ReadAllText(path).Value);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }

    [Fact]
    public void Listing_returns_the_configuration_file_names()
    {
        using var directory = new TemporaryDirectory();
        var fileSystem = TestFileSystems.SystemConfig();
        fileSystem.WriteAllText(Path.Combine(directory.Path, "wg0.conf"), "a");
        fileSystem.WriteAllText(Path.Combine(directory.Path, "wg1.conf"), "b");
        File.WriteAllText(Path.Combine(directory.Path, "notes.txt"), "c");

        var listing = fileSystem.ListFileNames(directory.Path);

        Assert.True(listing.IsSuccess);
        Assert.Equal(["wg0.conf", "wg1.conf"], listing.Value.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void Reading_a_missing_file_is_refused_as_a_value()
    {
        using var directory = new TemporaryDirectory();
        var fileSystem = TestFileSystems.SystemConfig();

        var result = fileSystem.ReadAllText(Path.Combine(directory.Path, "missing.conf"));

        Assert.True(result.IsFailure);
        Assert.Equal(WireGuardErrorCodes.ConfigNotFound, result.Error.Code);
    }

    [Fact]
    public void Deleting_a_file_removes_it_and_deleting_a_missing_one_succeeds()
    {
        using var directory = new TemporaryDirectory();
        var fileSystem = TestFileSystems.SystemConfig();
        var path = Path.Combine(directory.Path, "wg0.conf");
        fileSystem.WriteAllText(path, "content");

        Assert.True(fileSystem.DeleteFile(path).IsSuccess);
        Assert.False(File.Exists(path));
        Assert.True(fileSystem.DeleteFile(path).IsSuccess);
    }

    [Fact]
    public void Listing_a_missing_directory_is_refused_as_a_value()
    {
        using var directory = new TemporaryDirectory();
        var fileSystem = TestFileSystems.SystemConfig();

        var listing = fileSystem.ListFileNames(Path.Combine(directory.Path, "absent"));

        Assert.True(listing.IsFailure);
        Assert.Equal(WireGuardErrorCodes.ConfigListFailed, listing.Error.Code);
    }

    [Fact]
    public void A_directory_is_not_a_file()
    {
        using var directory = new TemporaryDirectory();
        var fileSystem = TestFileSystems.SystemConfig();

        Assert.True(fileSystem.DirectoryExists(directory.Path));
        Assert.False(fileSystem.FileExists(directory.Path));
    }
}

/// <summary>A directory under the temporary path that removes itself.</summary>
internal sealed class TemporaryDirectory : IDisposable
{
    internal TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wg-manager-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
        File.SetUnixFileMode(
            Path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    internal string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Cleanup is best effort; the directory is inside the temp path.
        }
    }
}
