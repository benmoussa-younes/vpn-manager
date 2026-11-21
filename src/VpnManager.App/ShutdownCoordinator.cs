namespace VpnManager.App;

/// <summary>
/// Runs the application shutdown exactly once, whichever path asks for it first:
/// the explicit quit command or the window being closed. Without this, a close
/// with the quit behaviour only closed the window and left the process running
/// with no window left to reach it.
/// </summary>
internal sealed class ShutdownCoordinator
{
    private readonly Func<ValueTask> _disposeShell;
    private readonly Func<ValueTask> _shutdownHost;
    private readonly Action _shutdownDesktop;
    private int _started;

    /// <summary>Initializes the coordinator with the three shutdown steps.</summary>
    /// <param name="disposeShell">Releases the shell and its presenter.</param>
    /// <param name="shutdownHost">Releases the host (log, journal, single instance).</param>
    /// <param name="shutdownDesktop">Ends the Avalonia desktop lifetime.</param>
    internal ShutdownCoordinator(
        Func<ValueTask> disposeShell,
        Func<ValueTask> shutdownHost,
        Action shutdownDesktop)
    {
        ArgumentNullException.ThrowIfNull(disposeShell);
        ArgumentNullException.ThrowIfNull(shutdownHost);
        ArgumentNullException.ThrowIfNull(shutdownDesktop);

        _disposeShell = disposeShell;
        _shutdownHost = shutdownHost;
        _shutdownDesktop = shutdownDesktop;
    }

    /// <summary>Gets whether shutdown has already started.</summary>
    internal bool IsStarted => Volatile.Read(ref _started) != 0;

    /// <summary>Runs the shutdown once; later callers return immediately.</summary>
    /// <returns>A task that completes when the steps have run.</returns>
    internal async ValueTask RunAsync()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        await _disposeShell().ConfigureAwait(true);
        await _shutdownHost().ConfigureAwait(true);
        _shutdownDesktop();
    }
}
