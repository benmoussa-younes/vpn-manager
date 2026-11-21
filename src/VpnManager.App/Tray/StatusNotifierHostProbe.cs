using System.Net.Sockets;
using Tmds.DBus.Protocol;

namespace VpnManager.App.Tray;

/// <summary>
/// Asks the StatusNotifierWatcher whether a tray host (the GNOME AppIndicator
/// extension, typically) is registered. Without one, an icon is never drawn, so
/// hiding the window on close would leave the application unreachable.
/// </summary>
internal static class StatusNotifierHostProbe
{
    /// <summary>The watcher name the AppIndicator host registers.</summary>
    internal const string WatcherName = "org.kde.StatusNotifierWatcher";

    /// <summary>The watcher object path.</summary>
    internal const string WatcherPath = "/org/kde/StatusNotifierWatcher";

    /// <summary>The property that says whether a host will display the icons.</summary>
    internal const string HostProperty = "IsStatusNotifierHostRegistered";

    /// <summary>
    /// Reads <see cref="HostProperty"/> from the watcher. Any failure — no
    /// session bus, no watcher, a watcher that does not answer — means no host.
    /// </summary>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns><see langword="true"/> when a tray host is registered.</returns>
    internal static async Task<bool> IsHostRegisteredAsync(CancellationToken cancellationToken = default)
    {
        if (DBusAddress.Session is not { Length: > 0 } address)
        {
            return false;
        }

        using var connection = new DBusConnection(address);
        try
        {
            await connection.ConnectAsync().ConfigureAwait(false);
            var writer = connection.GetMessageWriter();
            writer.WriteMethodCallHeader(
                WatcherName,
                WatcherPath,
                "org.freedesktop.DBus.Properties",
                "Get",
                "ss",
                MessageFlags.None);
            writer.WriteString(WatcherName);
            writer.WriteString(HostProperty);
            return await connection
                .CallMethodAsync(
                    writer.CreateMessage(),
                    static (message, _) =>
                    {
                        var reader = message.GetBodyReader();
                        return reader.ReadSignatureAsString() == "b" && reader.ReadBool();
                    },
                    null)
                .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is DBusExceptionBase or IOException or SocketException or InvalidOperationException or TimeoutException)
        {
            return false;
        }
    }
}
