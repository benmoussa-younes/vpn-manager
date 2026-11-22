using Core.Results;
using VpnManager.Core.Model;
using VpnManager.Core.Process;
using VpnManager.Core.Validation;

namespace VpnManager.Core.Runtime;

/// <summary>
/// The resolver state over <c>resolvectl status &lt;link&gt;</c>. The probe is
/// read-only: it reads where a link resolves names and never changes the resolver.
/// </summary>
public sealed class ResolvectlDnsStateReader : IDnsStateReader
{
    private const string Executable = "resolvectl";

    private readonly IProcessRunner _runner;

    /// <summary>Initializes the reader.</summary>
    /// <param name="runner">The process seam.</param>
    public ResolvectlDnsStateReader(IProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    /// <inheritdoc />
    public async Task<Result<DnsState>> ReadAsync(string interfaceName, CancellationToken cancellationToken = default)
    {
        if (!TunnelNameRules.IsValid(interfaceName))
        {
            return Result<DnsState>.Failure(WireGuardErrors.Validation(
                WireGuardErrorCodes.TunnelNameInvalid,
                $"'{interfaceName}' is not a usable interface name."));
        }

        ProcessResult result;

        try
        {
            result = await _runner
                .RunAsync(new ProcessRequest(Executable, ["--no-pager", "status", interfaceName]), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result<DnsState>.Failure(WireGuardErrors.ToolsUnavailable(Executable));
        }

        if (!result.Started)
        {
            return Result<DnsState>.Failure(WireGuardErrors.ToolsUnavailable(Executable));
        }

        if (result.ExitCode != 0)
        {
            return Result<DnsState>.Failure(WireGuardErrors.CommandFailed(
                string.Concat(Executable, " status ", interfaceName),
                result.ExitCode,
                result.StandardError));
        }

        return ResolvectlOutputParser.TryParse(result.StandardOutput, out var state)
            ? Result<DnsState>.Success(state)
            : Result<DnsState>.Failure(WireGuardErrors.Configuration(
                WireGuardErrorCodes.ParseError,
                string.Concat("'", Executable, " status ", interfaceName, "' did not report a DNS state.")));
    }
}
