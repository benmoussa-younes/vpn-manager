using System.Net.Sockets;
using Tmds.DBus.Protocol;

namespace VpnManager.App;

/// <summary>
/// Watches the GNOME session manager for the end-of-session hint. On logout or
/// shutdown the shell asks clients to close; a confirmation dialog would block
/// the session, so the close path answers "yes" without showing one when the
/// session is ending.
/// </summary>
internal sealed class SessionEndWatcher : IDisposable
{
    private const string Path = "/org/gnome/SessionManager";
    private const string Interface = "org.gnome.SessionManager";

    private const string Service = "org.gnome.SessionManager";

    private DBusConnection? _connection;
    private string? _clientId;
    private IDisposable? _query;
    private IDisposable? _end;
    private int _ending;

    /// <summary>Initializes a watcher that is not connected to a session bus.</summary>
    internal SessionEndWatcher()
    {
    }

    /// <summary>Gets whether the session is ending (logout or shutdown).</summary>
    internal bool IsEnding => Volatile.Read(ref _ending) != 0;

    /// <summary>
    /// Subscribes to the session manager's end-of-session signals. A missing bus
    /// or a session manager that is not GNOME leaves the watcher idle: the close
    /// path then asks as usual.
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
            _connection = connection;
            _clientId = await TryRegisterAsync(connection).ConfigureAwait(false);
            _query = await connection
                .AddMatchAsync(
                    Rule("QueryEndSession"),
                    _ => OnQueryEndSession(),
                    emitOnCapturedContext: false,
                    ObserverFlags.None,
                    state: null)
                .ConfigureAwait(false);
            _end = await connection
                .AddMatchAsync(
                    Rule("EndSession"),
                    _ => MarkEnding(),
                    emitOnCapturedContext: false,
                    ObserverFlags.None,
                    state: null)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is DBusExceptionBase or IOException or SocketException or InvalidOperationException)
        {
            _query?.Dispose();
            _end?.Dispose();
            _query = null;
            _end = null;
            _connection = null;
            connection.Dispose();
        }
    }

    private static async Task<string?> TryRegisterAsync(DBusConnection connection)
    {
        try
        {
            // gnome-session only waits for registered clients; the broadcast
            // signals still drive the watcher when registration is unavailable.
            var writer = connection.GetMessageWriter();
            writer.WriteMethodCallHeader(
                Service,
                Path,
                Interface,
                "RegisterClient",
                "ss",
                MessageFlags.None);
            writer.WriteString("VPN Manager");
            writer.WriteString("vpn-manager");
            return await connection
                .CallMethodAsync(
                    writer.CreateMessage(),
                    static (message, _) => message.GetBodyReader().ReadString(),
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
        MarkEnding();
        if (_clientId is { } clientId && _connection is { } connection)
        {
            _ = RespondAsync(connection, clientId);
        }
    }

    private static async Task RespondAsync(DBusConnection connection, string clientId)
    {
        try
        {
            var writer = connection.GetMessageWriter();
            writer.WriteMethodCallHeader(
                Service,
                Path,
                Interface,
                "EndSessionResponse",
                "sbs",
                MessageFlags.None);
            writer.WriteString(clientId);
            writer.WriteBool(true);
            writer.WriteString(string.Empty);
            await connection.CallMethodAsync(writer.CreateMessage()).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is DBusExceptionBase or IOException or SocketException or InvalidOperationException)
        {
            // The session is ending anyway; a lost response only delays it.
        }
    }

    /// <summary>Marks the session as ending; the signal handler uses it.</summary>
    internal void MarkEnding() => Volatile.Write(ref _ending, 1);

    /// <inheritdoc />
    public void Dispose()
    {
        _query?.Dispose();
        _end?.Dispose();
        _connection?.Dispose();
        _query = null;
        _end = null;
        _connection = null;
    }

    private static MatchRule Rule(string member) => new()
    {
        Type = MessageType.Signal,
        Interface = Interface,
        Path = Path,
        Member = member,
    };
}
