using Core.Results;
using VpnManager.Core.State;

namespace VpnManager.Core.Tests.Fakes;

/// <summary>An in-memory application-state store whose contents the test chooses.</summary>
internal sealed class FakeApplicationStateStore : IApplicationStateStore
{
    internal ApplicationState State { get; set; } = new();

    internal int Saves { get; private set; }

    internal bool FailLoad { get; set; }

    public Result<ApplicationState> Load() => FailLoad
        ? Result<ApplicationState>.Failure(new CoreError("test.state_load", "the state could not be read"))
        : Result<ApplicationState>.Success(State);

    public Result<string> Save(ApplicationState state)
    {
        State = state;
        Saves++;
        return Result<string>.Success("/tmp/vpnmanager/vpn-manager/state.json");
    }

    public Result<string> Export(string path) => Result<string>.Success(path);

    public Result<ApplicationState> LoadFrom(string path) => Result<ApplicationState>.Success(State);
}
