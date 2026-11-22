using Core.Results;
using VpnManager.Core.State;
using CoreJournal = Core.Diagnostics.IOperationJournal;

namespace VpnManager.App;

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
