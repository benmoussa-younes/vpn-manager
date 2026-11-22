using System.Net.Sockets;

namespace VpnManager.Core.Services;

/// <summary>
/// Keeps one instance of the application per user. The first instance binds a
/// Unix domain socket; a later launch connects to it, asks it to show its window
/// and exits, so there is never a second window.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private const byte ActivationByte = 1;

    private readonly string _socketPath;
    private Socket? _listener;
    private CancellationTokenSource? _lifetime;
    private bool _disposed;

    /// <summary>Initializes a guard bound to a socket path.</summary>
    /// <param name="socketPath">The Unix domain socket the guard owns.</param>
    public SingleInstanceGuard(string socketPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(socketPath);
        _socketPath = socketPath;
    }

    /// <summary>
    /// Raised when another launch asked this instance to surface. It is raised on a
    /// background thread; a UI host must marshal it to its own thread.
    /// </summary>
    public event Action? ActivationRequested;

    /// <summary>The socket path for the current user and session.</summary>
    /// <returns>An absolute path under the user's runtime directory.</returns>
    public static string DefaultSocketPath()
    {
        var runtimeDirectory = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        var directory = string.IsNullOrEmpty(runtimeDirectory) ? Path.GetTempPath() : runtimeDirectory;
        return Path.Combine(directory, $"vpn-manager-{Environment.UserName}.sock");
    }

    /// <summary>
    /// Takes the single-instance claim. Returns <see langword="true"/> when this is
    /// the only instance; returns <see langword="false"/> after asking the running
    /// instance to surface, in which case the caller must exit.
    /// </summary>
    /// <returns>Whether this process became the running instance.</returns>
    public bool TryAcquire()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (TryActivateRunningInstance())
        {
            return false;
        }

        // A socket file that nobody answers is a leftover from a crash.
        DeleteSocketFile();

        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
        }
        catch (SocketException)
        {
            // Another instance bound the path between the check above and here; it owns
            // the claim, so this process must exit instead of crashing.
            listener.Dispose();
            return false;
        }

        SetOwnerOnly(_socketPath);
        listener.Listen(1);
        _listener = listener;

        _lifetime = new CancellationTokenSource();
        _ = AcceptLoopAsync(listener, _lifetime.Token);
        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _lifetime?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The token source is already gone; nothing to cancel.
        }

        _lifetime?.Dispose();
        _listener?.Dispose();
        DeleteSocketFile();
    }

    private bool TryActivateRunningInstance()
    {
        if (!File.Exists(_socketPath))
        {
            return false;
        }

        try
        {
            using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            client.Connect(new UnixDomainSocketEndPoint(_socketPath));
            client.Send([ActivationByte]);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private async Task AcceptLoopAsync(Socket listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Socket connection;
            try
            {
                connection = await listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            try
            {
                using (connection)
                {
                    var buffer = new byte[1];
                    await connection.ReceiveAsync(buffer, SocketFlags.None, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                // The asking process may close early; surfacing is still the intent.
            }

            ActivationRequested?.Invoke();
        }
    }

    private void DeleteSocketFile()
    {
        try
        {
            if (File.Exists(_socketPath))
            {
                File.Delete(_socketPath);
            }
        }
        catch (IOException)
        {
            // A concurrent process may hold it; the bind below decides.
        }
        catch (UnauthorizedAccessException)
        {
            // Nothing can be done; the bind below decides.
        }
    }

    private static void SetOwnerOnly(string path)
    {
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The socket stays usable; its directory is expected to be private.
        }
    }
}
