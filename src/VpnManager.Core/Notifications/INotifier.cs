namespace VpnManager.Core.Notifications;

/// <summary>
/// The desktop notification boundary. It is a seam so the connect and disconnect
/// feedback is testable without a notification daemon.
/// </summary>
public interface INotifier
{
    /// <summary>Shows a desktop notification. A notification is best effort.</summary>
    /// <param name="title">The summary line.</param>
    /// <param name="body">The detail line.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes when the notification was sent.</returns>
    Task NotifyAsync(string title, string body, CancellationToken cancellationToken = default);
}
