using Core.Results;

namespace VpnManager.Core.State;

/// <summary>
/// An append-only record of what the application did. It is a seam because it
/// crosses the file-system boundary; the Core states what to record, the adapter
/// decides where it goes.
/// </summary>
public interface IOperationJournal
{
    /// <summary>Appends one line to the journal.</summary>
    /// <param name="message">The message; it must never contain a secret.</param>
    /// <returns>The journal path, or a failure.</returns>
    Result<string> Record(string message);
}
