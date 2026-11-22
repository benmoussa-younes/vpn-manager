using VpnManager.Core.Services;

namespace VpnManager.Core.Tests;

/// <summary>The single-instance guard.</summary>
public sealed class SingleInstanceGuardTests
{
    [Fact]
    public void The_first_instance_acquires_and_the_second_activates_it()
    {
        var path = TemporarySocketPath();
        using var first = new SingleInstanceGuard(path);
        Assert.True(first.TryAcquire());

        using var activated = new ManualResetEventSlim(false);
        first.ActivationRequested += activated.Set;

        using var second = new SingleInstanceGuard(path);
        Assert.False(second.TryAcquire());

        Assert.True(activated.Wait(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void A_stale_socket_file_is_replaced_and_dispose_removes_it()
    {
        var path = TemporarySocketPath();
        File.WriteAllText(path, "left by a crash");

        using (var guard = new SingleInstanceGuard(path))
        {
            Assert.True(guard.TryAcquire());
            Assert.True(File.Exists(path));
        }

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void The_default_path_is_named_after_the_application_and_the_user()
    {
        var path = SingleInstanceGuard.DefaultSocketPath();

        Assert.True(Path.IsPathRooted(path));
        Assert.Equal($"vpn-manager-{Environment.UserName}.sock", Path.GetFileName(path));
    }

    [Fact]
    public void An_empty_path_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new SingleInstanceGuard("  "));
    }

    private static string TemporarySocketPath() =>
        Path.Combine(Path.GetTempPath(), $"wgm-{Guid.NewGuid():N}.sock");
}
