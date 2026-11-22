using Core.Results;
using VpnManager.Core.State;

namespace VpnManager.Core.Tests.Fakes;

/// <summary>An operation journal that records its lines in memory.</summary>
internal sealed class FakeOperationJournal : IOperationJournal
{
    internal List<string> Entries { get; } = [];

    public Result<string> Record(string message)
    {
        Entries.Add(message);
        return Result<string>.Success("/tmp/benmoussa/vpn-manager/journal.log");
    }
}
