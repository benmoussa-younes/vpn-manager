using System.ComponentModel;
using System.Diagnostics;

namespace VpnManager.Core.Process;

/// <summary>
/// The real process runner. It starts the executable directly with an argument
/// vector: <c>UseShellExecute</c> is false, no shell is spawned, and no
/// privilege-escalation program is ever invoked here.
/// </summary>
public sealed class SystemProcessRunner : IProcessRunner
{
    // A bare tool name is resolved against these directories, never against the
    // caller's PATH, so a writable directory earlier in PATH cannot shadow nmcli,
    // wg or notify-send with a rogue binary.
    private static readonly string[] TrustedDirectories =
        ["/usr/bin", "/bin", "/usr/sbin", "/sbin", "/usr/local/bin"];

    /// <inheritdoc />
    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var executable = ResolveExecutable(request.FileName);
        if (executable is null)
        {
            return ProcessResult.NotStarted($"'{request.FileName}' was not found in the trusted system directories.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = request.StandardInput is not null,
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (request.Environment is not null)
        {
            foreach (var (key, value) in request.Environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = new System.Diagnostics.Process { StartInfo = startInfo };

        try
        {
            if (!process.Start())
            {
                return ProcessResult.NotStarted($"'{executable}' could not be started.");
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return ProcessResult.NotStarted(exception.Message);
        }

        if (request.StandardInput is not null)
        {
            await process.StandardInput.WriteAsync(request.StandardInput).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            process.StandardInput.Close();
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return ProcessResult.Completed(process.ExitCode, await standardOutput.ConfigureAwait(false), await standardError.ConfigureAwait(false));
    }

    private static string? ResolveExecutable(string fileName)
    {
        if (fileName.Contains('/'))
        {
            return fileName;
        }

        foreach (var directory in TrustedDirectories)
        {
            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
