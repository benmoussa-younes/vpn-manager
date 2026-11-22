using Core.Results;

namespace VpnManager.Core.State;

/// <summary>
/// The application's persisted state. It is a seam because it crosses the
/// file-system boundary; the Core decides what to remember, the adapter decides
/// where and how to write it.
/// </summary>
public interface IApplicationStateStore
{
    /// <summary>Reads the saved state.</summary>
    /// <returns>The state, or a failure; an absent state is an empty state, not an error.</returns>
    Result<ApplicationState> Load();

    /// <summary>Writes the state atomically.</summary>
    /// <param name="state">The state to save.</param>
    /// <returns>The written path, or a failure.</returns>
    Result<string> Save(ApplicationState state);

    /// <summary>Writes the current state to an arbitrary path (a backup).</summary>
    /// <param name="path">The destination path.</param>
    /// <returns>The written path, or a failure.</returns>
    Result<string> Export(string path);

    /// <summary>Reads a state document from an arbitrary path (a restore).</summary>
    /// <param name="path">The source path.</param>
    /// <returns>The state, or a failure.</returns>
    Result<ApplicationState> LoadFrom(string path);
}
