using System.Net.Sockets;
using Tmds.DBus.Protocol;

namespace VpnManager.App;

/// <summary>
/// Registers as a GNOME session client and answers the end-of-session protocol.
/// gnome-session waits for every registered client to answer, so an application
/// that registers and never replies holds a logout or a shutdown open until the
/// timeout. The watcher answers <c>QueryEndSession</c> at once, asks the window to
/// close on <c>EndSession</c> and on <c>Stop</c>, and leaves the disconnect dialog
/// out of that path.
/// </summary>
internal sealed class SessionEndWatcher : IDisposable
{
    private const string Service = "org.gnome.SessionManager";
    private const string ManagerPath = "/org/gnome/SessionManager";
    private const string ManagerInterface = "org.gnome.SessionManager";
    private const string ClientInterface = "org.gnome.SessionManager.ClientPrivate";

    private readonly string _service;
    private DBusConnection? _connection;
    private string? _clientPath;
    private IDisposable? _stop;
    private IDisposable? _query;
    private IDisposable? _end;
    private IDisposable? _cancel;
    private int _ending;
    private int _closing;

    /// <summary>Initializes a watcher that is not connected to a session bus.</summary>
    /// <param name="service">The session-manager name; a test owns its own on the bus.</param>
    internal SessionEndWatcher(string service = Service)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(service);
        _service = service;
    }

    /// <summary>Gets whether the session is ending (logout or shutdown).</summary>
    internal bool IsEnding => Volatile.Read(ref _ending) != 0;

    /// <summary>
    /// Registers with the session manager and subscribes to the signals it sends
    /// to the client object it returns. A missing bus or a session manager that is
    /// not GNOME leaves the watcher idle: the close path then asks as usual.
    /// </summary>
    /// <returns>A task that completes when the subscription is installed.</returns>
    internal async Task StartAsync()
    {
        if (_connection is not null || DBusAddress.Session is not { Length: > 0 } address)
        {
            return;
        }

        var connection = new DBusConnection(address);
        try
        {
            await connection.ConnectAsync().ConfigureAwait(false);
            var clientPath = await RegisterAsync(connection).ConfigureAwait(false);
            if (clientPath is null)
            {
                connection.Dispose();
                return;
            }

            _connection = connection;
            _clientPath = clientPath;

            // The signals travel on the client object, not on the manager: the
            // manager only emits ClientAdded and friends.
            _stop = await connection
                .AddMatchAsync(Rule(clientPath, "Stop"), _ => OnEndSession(), emitOnCapturedContext: false, ObserverFlags.None, state: null)
                .ConfigureAwait(false);
            _query = await connection
                .AddMatchAsync(Rule(clientPath, "QueryEndSession"), _ => OnQueryEndSession(), emitOnCapturedContext: false, ObserverFlags.None, state: null)
                .ConfigureAwait(false);
            _end = await connection
                .AddMatchAsync(Rule(clientPath, "EndSession"), _ => OnEndSession(), emitOnCapturedContext: false, ObserverFlags.None, state: null)
                .ConfigureAwait(false);
            _cancel = await connection
                .AddMatchAsync(Rule(clientPath, "CancelEndSession"), _ => OnCancelEndSession(), emitOnCapturedContext: false, ObserverFlags.None, state: null)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is DBusExceptionBase or IOException or SocketException or InvalidOperationException)
        {
            _query?.Dispose();
            _end?.Dispose();
            _stop?.Dispose();
            _cancel?.Dispose();
            _query = null;
            _end = null;
            _stop = null;
            _cancel = null;
            _connection = null;
            _clientPath = null;
            connection.Dispose();
        }
    }

    /// <summary>Raised once when the session manager asks the application to quit.</summary>
    internal event Action? Ending;

    /// <summary>Marks the session as ending without closing the window; the tests use it.</summary>
    internal void MarkEnding() => Interlocked.Exchange(ref _ending, 1);

    /// <inheritdoc />
    public void Dispose()
    {
        _query?.Dispose();
        _end?.Dispose();
        _stop?.Dispose();
        _cancel?.Dispose();
        _connection?.Dispose();
        _query = null;
        _end = null;
        _stop = null;
        _cancel = null;
        _connection = null;
        _clientPath = null;
    }

    private async Task<string?> RegisterAsync(DBusConnection connection)
    {
        try
        {
            // RegisterClient answers with the object path of the client. Reading it
            // as a string fails on the wire type, which is why the reply is an
            // object path: without it the client can neither be addressed nor
            // answer, and gnome-session waits for it.
            var writer = connection.GetMessageWriter();
            writer.WriteMethodCallHeader(
                _service,
                ManagerPath,
                ManagerInterface,
                "RegisterClient",
                "ss",
                MessageFlags.None);
            writer.WriteString("VPN Manager");
            writer.WriteString(string.Empty);
            return await connection
                .CallMethodAsync(
                    writer.CreateMessage(),
                    static (message, _) => message.GetBodyReader().ReadObjectPathAsString(),
                    null)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is DBusExceptionBase or IOException or SocketException or InvalidOperationException)
        {
            return null;
        }
    }

    private void OnQueryEndSession()
    {
        // The shell may still cancel the logout: the dialog is bypassed, but the
        // window waits for EndSession before it closes.
        MarkEnding();
        _ = RespondAsync();
    }

    private void OnEndSession()
    {
        Interlocked.Exchange(ref _ending, 1);
        if (Interlocked.Exchange(ref _closing, 1) != 0)
        {
            return;
        }

        Ending?.Invoke();
    }

    private void OnCancelEndSession() => Interlocked.Exchange(ref _ending, 0);

    private async Task RespondAsync()
    {
        var connection = _connection;
        var clientPath = _clientPath;
        if (connection is null || clientPath is null)
        {
            return;
        }

        try
        {
            // EndSessionResponse lives on the client object, signature (bs).
            var writer = connection.GetMessageWriter();
            writer.WriteMethodCallHeader(
                _service,
                clientPath,
                ClientInterface,
                "EndSessionResponse",
                "bs",
                MessageFlags.None);
            writer.WriteBool(true);
            writer.WriteString(string.Empty);
            await connection.CallMethodAsync(writer.CreateMessage()).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is DBusExceptionBase or IOException or SocketException or InvalidOperationException)
        {
            // The session is ending anyway; a lost response only delays it.
        }
    }

    private static MatchRule Rule(string path, string member) => new()
    {
        Type = MessageType.Signal,
        Interface = ClientInterface,
        Path = path,
        Member = member,
    };
}
