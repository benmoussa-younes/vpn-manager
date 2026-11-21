using VpnManager.Core.Process;

namespace VpnManager.Core.Notifications;

/// <summary>
/// The GNOME notification over <c>notify-send</c>: no D-Bus binding is needed, so
/// the Native AOT build stays dependency-free. A notification is best effort, so a
/// failure is swallowed rather than surfaced on the status line.
/// </summary>
public sealed class NotifySendNotifier : INotifier
{
    private const string Executable = "notify-send";

    private readonly IProcessRunner _runner;

    /// <summary>Initializes the notifier.</summary>
    /// <param name="runner">The process seam.</param>
    public NotifySendNotifier(IProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    /// <inheritdoc />
    public async Task NotifyAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(body);

        try
        {
            // --app-name labels the notification with the product name.
            await _runner
                .RunAsync(new ProcessRequest(Executable, ["--app-name", "VPN Manager", title, body]), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // No notification daemon, or a refused process: the tunnel operation
            // still succeeded and the window already shows the outcome.
        }
    }
}
