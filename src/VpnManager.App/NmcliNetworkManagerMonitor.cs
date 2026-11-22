using System.Diagnostics;

namespace VpnManager.App;

/// <summary>
/// A long-lived <c>nmcli monitor</c> process: NetworkManager pushes a line when a
/// device or connection changes, so the window can refresh immediately instead of
/// waiting for the next poll. The poll remains the fallback when the process cannot
/// start or exits.
/// </summary>
internal sealed class NmcliNetworkManagerMonitor : IDisposable
{
    private const string Executable = "/usr/bin/nmcli";

    private Process? _process;
    private int _disposed;

    /// <summary>Raised on a non-empty monitor line; the subscriber marshals to its UI.</summary>
    internal event Action? Changed;

    /// <summary>Starts the monitor; a failure leaves the polling fallback in place.</summary>
    internal void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (_process is not null || !File.Exists(Executable))
        {
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = Executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("monitor");

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) => OnLine(args.Data);
        process.ErrorDataReceived += (_, args) => OnLine(args.Data);

        try
        {
            if (!process.Start())
            {
                process.Dispose();
                return;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _process = process;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            process.Dispose();
        }
    }

    private void OnLine(string? line)
    {
        if (Volatile.Read(ref _disposed) != 0 || string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        Changed?.Invoke();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        var process = _process;
        _process = null;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // The monitor is best effort; shutdown must not fail on it.
        }
        finally
        {
            process.Dispose();
        }
    }
}
