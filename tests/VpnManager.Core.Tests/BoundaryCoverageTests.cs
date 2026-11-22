using VpnManager.Core.Logging;
using VpnManager.Core.NetworkManager;
using VpnManager.Core.Process;
using VpnManager.Core.Tests.Fakes;

namespace VpnManager.Core.Tests;

/// <summary>
/// The failure arms of the process and notification boundaries: a best-effort
/// contract that must swallow a refused spawn, a missing executable or an
/// unwritable log directory rather than propagate.
/// </summary>
public sealed class BoundaryCoverageTests
{
    [Fact]
    public async Task An_executable_that_cannot_start_is_reported_not_started()
    {
        var result = await new SystemProcessRunner()
            .RunAsync(new ProcessRequest("/nonexistent/wgm-no-such-binary", []));

        Assert.False(result.Started);
    }

    [Fact]
    public void The_logger_swallows_a_directory_it_cannot_create()
    {
        var file = Path.GetTempFileName();
        try
        {
            using var logger = new RotatingFileLogger(file, "application.log");

            logger.Log(LogLevel.Information, "hello");
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task An_nmcli_import_failure_is_reported()
    {
        var runner = new FakeProcessRunner(request =>
            request.Arguments.Contains("import")
                ? ProcessResult.Completed(1, string.Empty, "the import failed")
                : ProcessResult.Completed(0, string.Empty, string.Empty));
        var connections = new NmcliNetworkManagerConnections(runner);

        var result = await connections.EnsureAsync("wgtest", "wgtest", "/tmp/wgtest.conf", "workstation");

        Assert.True(result.IsFailure);
    }
}
