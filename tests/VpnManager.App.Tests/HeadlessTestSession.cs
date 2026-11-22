using Avalonia.Headless;

[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]

namespace VpnManager.App.Tests;

/// <summary>One shared headless Avalonia session for the UI tests.</summary>
internal static class HeadlessTestSession
{
    private static readonly Lazy<HeadlessUnitTestSession> Session = new(
        () => HeadlessUnitTestSession.StartNew(typeof(global::VpnManager.App.App)));

    internal static Task RunAsync(Action action) =>
        Session.Value.Dispatch(action, CancellationToken.None);

    internal static Task RunAsync(Func<Task> action) =>
        Session.Value.Dispatch(
            async () =>
            {
                await action().ConfigureAwait(true);
                return true;
            },
            CancellationToken.None);
}
