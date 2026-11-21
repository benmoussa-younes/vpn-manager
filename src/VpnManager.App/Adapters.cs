using Core.Results;
using VpnManager.Core.Notifications;
using VpnManager.Core.State;
using CoreJournal = Core.Diagnostics.IOperationJournal;
using CoreNotifier = Core.Notifications.ISystemNotifier;

namespace VpnManager.App;

/// <summary>Projects the Core desktop notifier onto the product's notifier seam.</summary>
internal sealed class CoreNotifierAdapter : INotifier
{
    private readonly CoreNotifier _inner;

    internal CoreNotifierAdapter(CoreNotifier inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <inheritdoc />
    public Task NotifyAsync(string title, string body, CancellationToken cancellationToken = default) =>
        _inner.NotifyAsync(title, body, cancellationToken).AsTask();
}

/// <summary>Projects the Core operation journal onto the product's journal seam.</summary>
internal sealed class CoreOperationJournalAdapter : IOperationJournal
{
    private readonly CoreJournal _inner;

    internal CoreOperationJournalAdapter(CoreJournal inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <inheritdoc />
    public Result<string> Record(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        _inner.Record("vpn-manager.operation", message);
        return Result<string>.Success(string.Empty);
    }
}
