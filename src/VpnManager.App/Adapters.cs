using Core.Results;
using VpnManager.Core.Notifications;
using VpnManager.Core.Process;
using VpnManager.Core.State;
using CoreJournal = Core.Diagnostics.IOperationJournal;
using CoreNotifier = Core.Notifications.ISystemNotifier;
using CoreProcessRequest = Core.Execution.ProcessRequest;
using CoreProcessRunner = Core.Execution.IProcessRunner;

namespace VpnManager.App;

/// <summary>
/// Projects the Core process contract onto the product's process seam: the
/// product keeps its semantic command model, while the real start, timeout,
/// output cap and trusted-directory resolution are Core's.
/// </summary>
internal sealed class CoreProcessRunnerAdapter : IProcessRunner
{
    private readonly CoreProcessRunner _inner;

    internal CoreProcessRunnerAdapter(CoreProcessRunner inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <inheritdoc />
    public async Task<ProcessResult> RunAsync(
        ProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var run = await _inner.RunAsync(
            new CoreProcessRequest(
                request.FileName,
                request.Arguments,
                request.StandardInput,
                request.Environment),
            cancellationToken).ConfigureAwait(false);

        if (run.TryGetError(out var error))
        {
            return ProcessResult.NotStarted(error.Message);
        }

        var completed = run.Value;
        return ProcessResult.Completed(
            completed.ExitCode,
            completed.StandardOutput,
            completed.StandardError);
    }
}

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
