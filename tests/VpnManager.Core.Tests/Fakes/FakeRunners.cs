using Core.Results;
using VpnManager.Core.Process;
using VpnManager.Core.Runtime;

namespace VpnManager.Core.Tests.Fakes;

/// <summary>A process seam that records the request and returns a scripted result.</summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Func<ProcessRequest, ProcessResult> _responder;

    internal FakeProcessRunner(Func<ProcessRequest, ProcessResult> responder) => _responder = responder;

    internal List<ProcessRequest> Requests { get; } = [];

    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return Task.FromResult(_responder(request));
    }

    /// <summary>A runner whose executable cannot be started.</summary>
    internal static FakeProcessRunner NotStarted() => new(_ => ProcessResult.NotStarted("No such file or directory"));

    /// <summary>A runner that always completes with one exit code and streams.</summary>
    internal static FakeProcessRunner Exiting(int exitCode, string stdout = "", string stderr = "") =>
        new(_ => ProcessResult.Completed(exitCode, stdout, stderr));
}

/// <summary>A command boundary that records every semantic command it is asked to run.</summary>
internal sealed class FakeWireGuardCommandRunner : IWireGuardCommandRunner
{
    private readonly Func<WireGuardCommand, Result<CommandOutput>> _responder;

    internal FakeWireGuardCommandRunner(Func<WireGuardCommand, Result<CommandOutput>> responder) => _responder = responder;

    internal List<WireGuardCommand> Commands { get; } = [];

    /// <summary>The commands whose argument vector ends with the given argument.</summary>
    internal IEnumerable<WireGuardCommand> CommandsEndingWith(string argument) =>
        Commands.Where(command => command.Arguments.Count > 0 && command.Arguments[^1] == argument);

    public Task<Result<CommandOutput>> RunAsync(WireGuardCommand command, CancellationToken cancellationToken = default)
    {
        Commands.Add(command);
        return Task.FromResult(_responder(command));
    }

    /// <summary>Answers every command with the same standard output.</summary>
    internal static FakeWireGuardCommandRunner Returning(string stdout) =>
        new(_ => Result<CommandOutput>.Success(new CommandOutput(0, stdout, string.Empty)));

    /// <summary>Answers every command with the same failure.</summary>
    internal static FakeWireGuardCommandRunner Failing(CoreError error) =>
        new(_ => Result<CommandOutput>.Failure(error));

    /// <summary>Answers per command, keyed on the first argument.</summary>
    internal static FakeWireGuardCommandRunner ByVerb(
        string showOutput,
        string interfacesOutput,
        Func<WireGuardCommand, Result<CommandOutput>?>? fallback = null) =>
        new(command =>
        {
            if (command.Arguments.Count > 0 && command.Arguments[0] == "show")
            {
                return command.Arguments.Count == 2 && command.Arguments[1] == "interfaces"
                    ? Result<CommandOutput>.Success(new CommandOutput(0, interfacesOutput, string.Empty))
                    : Result<CommandOutput>.Success(new CommandOutput(0, showOutput, string.Empty));
            }

            return fallback?.Invoke(command) ?? Result<CommandOutput>.Success(new CommandOutput(0, string.Empty, string.Empty));
        });

    /// <summary>Builds a runner whose responses are chosen by the test.</summary>
    internal static FakeWireGuardCommandRunner FromResponder(Func<WireGuardCommand, Result<CommandOutput>> responder) => new(responder);
}
