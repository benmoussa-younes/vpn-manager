using System.Text.Json.Serialization;
using Core.Mvp.Services;
using Core.Platform;
using Core.Results;
using VpnManager.Core.State;

namespace VpnManager.App.State;

/// <summary>
/// The application state as one typed JSON document through
/// <see cref="JsonSettingsStore{T}"/>: atomic, owner-only and bounded. The same
/// document can be exported to, and restored from, an arbitrary path.
/// </summary>
internal sealed class JsonApplicationStateStore : IApplicationStateStore
{
    private const string StateName = "state.json";

    private readonly string _path;
    private readonly IAtomicFileWriterFactory _atomicFiles;
    private readonly IFileSystem _fileSystem;
    private readonly JsonSettingsStore<ApplicationState> _store;

    internal JsonApplicationStateStore(
        string path,
        IAtomicFileWriterFactory atomicFiles,
        IFileSystem fileSystem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(atomicFiles);
        ArgumentNullException.ThrowIfNull(fileSystem);

        _path = path;
        _atomicFiles = atomicFiles;
        _fileSystem = fileSystem;
        _store = new JsonSettingsStore<ApplicationState>(
            path,
            StateJsonContext.Default.ApplicationState,
            atomicFiles,
            fileSystem);
    }

    /// <inheritdoc />
    public Result<ApplicationState> Load()
    {
        var state = _store.Load();
        if (state is not null)
        {
            return Result<ApplicationState>.Success(state);
        }

        // A missing file is an empty state; an existing one that did not load is a
        // corrupt file the caller reports instead of silently overwriting.
        return File.Exists(_path)
            ? Result<ApplicationState>.Failure(new CoreError(
                "state.corrupt",
                "The state file exists but could not be read."))
            : Result<ApplicationState>.Success(new ApplicationState());
    }

    /// <inheritdoc />
    public Result<string> Save(ApplicationState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        try
        {
            _store.Save(state);
            return Result<string>.Success(_path);
        }
        catch (IOException exception)
        {
            return Result<string>.Failure(new CoreError("state.write_failed", exception.Message));
        }
    }

    /// <inheritdoc />
    public Result<string> Export(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var state = Load();
        if (!state.TryGetValue(out var value))
        {
            return Result<string>.Failure(state.Error);
        }

        try
        {
            var store = new JsonSettingsStore<ApplicationState>(
                Path.GetFullPath(path),
                StateJsonContext.Default.ApplicationState,
                _atomicFiles,
                _fileSystem);
            store.Save(value);
            return Result<string>.Success(Path.GetFullPath(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Result<string>.Failure(new CoreError("state.export_failed", exception.Message));
        }
    }

    /// <inheritdoc />
    public Result<ApplicationState> LoadFrom(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            var store = new JsonSettingsStore<ApplicationState>(
                Path.GetFullPath(path),
                StateJsonContext.Default.ApplicationState,
                _atomicFiles,
                _fileSystem);
            var state = store.Load();
            return state is null
                ? Result<ApplicationState>.Failure(new CoreError(
                    "state.restore_failed",
                    $"'{path}' is not a readable state document."))
                : Result<ApplicationState>.Success(state);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return Result<ApplicationState>.Failure(new CoreError("state.restore_failed", exception.Message));
        }
    }
}

/// <summary>The source-generated JSON context, so serialization stays AOT-safe.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ApplicationState))]
internal sealed partial class StateJsonContext : JsonSerializerContext
{
}
