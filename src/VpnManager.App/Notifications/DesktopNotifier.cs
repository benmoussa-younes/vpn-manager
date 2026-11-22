using System.Net.Sockets;
using Core.Diagnostics;
using Tmds.DBus.Protocol;
using VpnManager.Core.Notifications;

namespace VpnManager.App.Notifications;

/// <summary>
/// A desktop notification over the session bus (<c>org.freedesktop.Notifications</c>).
/// A notification is best effort: a missing daemon, a bus that went away or a
/// refused call is swallowed, because the tunnel operation already succeeded and
/// the window shows the outcome. There is no <c>notify-send</c> dependency: the
/// daemon is the interface GNOME exposes.
/// </summary>
internal sealed class DesktopNotifier : INotifier
{
    private const string Service = "org.freedesktop.Notifications";
    private const string ObjectPath = "/org/freedesktop/Notifications";
    private const string Interface = "org.freedesktop.Notifications";
    private const string Signature = "susssasa{sv}i";

    private readonly string _service;
    private readonly IApplicationLogger? _logger;
    private DBusConnection? _connection;

    /// <summary>Initializes the notifier.</summary>
    /// <param name="service">The notification service name; a test owns its own on the bus.</param>
    /// <param name="logger">The diagnostic log, or <see langword="null"/> in a test.</param>
    internal DesktopNotifier(string service = Service, IApplicationLogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(service);
        _service = service;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task NotifyAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(body);

        try
        {
            var connection = await GetConnectionAsync().ConfigureAwait(false);
            if (connection is null)
            {
                return;
            }

            var writer = connection.GetMessageWriter();
            writer.WriteMethodCallHeader(_service, ObjectPath, Interface, "Notify", Signature, MessageFlags.None);
            writer.WriteString("VPN Manager");
            writer.WriteUInt32(0);
            writer.WriteString(string.Empty);
            writer.WriteString(title);
            writer.WriteString(body);
            writer.WriteArray(Array.Empty<string>());
            var hints = writer.WriteArrayStart(DBusType.DictEntry);
            writer.WriteArrayEnd(hints);
            writer.WriteInt32(-1);
            await connection.CallMethodAsync(writer.CreateMessage()).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is DBusExceptionBase or IOException or SocketException or InvalidOperationException)
        {
            // The next notification reconnects; the tunnel state is unchanged.
            ResetConnection();
            _logger?.Log(LogLevel.Warning, string.Concat("desktop notification failed: ", exception.GetType().Name));
        }
    }

    private async Task<DBusConnection?> GetConnectionAsync()
    {
        if (_connection is { } existing)
        {
            return existing;
        }

        if (DBusAddress.Session is not { Length: > 0 } address)
        {
            return null;
        }

        var connection = new DBusConnection(address);
        try
        {
            await connection.ConnectAsync().ConfigureAwait(false);
            _connection = connection;
            return connection;
        }
        catch (Exception exception) when (exception is DBusExceptionBase or IOException or SocketException or InvalidOperationException)
        {
            connection.Dispose();
            return null;
        }
    }

    private void ResetConnection()
    {
        _connection?.Dispose();
        _connection = null;
    }
}
