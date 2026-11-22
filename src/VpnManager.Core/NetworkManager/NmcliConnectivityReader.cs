using VpnManager.Core.Process;

namespace VpnManager.Core.NetworkManager;

/// <summary>
/// Reads the connectivity NetworkManager reports (<c>nmcli general</c>). It is
/// best effort: a missing <c>nmcli</c>, a refused call or an unknown word reads as
/// <see cref="NetworkConnectivity.Unknown"/>, which raises no notification.
/// </summary>
public sealed class NmcliConnectivityReader : IConnectivityReader
{
    private const string Executable = "nmcli";

    private readonly IProcessRunner _runner;

    /// <summary>Initializes the reader.</summary>
    /// <param name="runner">The process seam.</param>
    public NmcliConnectivityReader(IProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    /// <inheritdoc />
    public async Task<NetworkConnectivity> ReadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _runner
                .RunAsync(new ProcessRequest(Executable, ["-t", "-f", "CONNECTIVITY,STATE", "general"]), cancellationToken)
                .ConfigureAwait(false);

            return result.Started && result.ExitCode == 0 ? Parse(result.StandardOutput) : NetworkConnectivity.Unknown;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return NetworkConnectivity.Unknown;
        }
    }

    /// <summary>Parses the terse output of <c>nmcli -f CONNECTIVITY,STATE general</c>.</summary>
    /// <param name="output">The captured output, for example <c>full:connected</c>.</param>
    /// <returns>
    /// The state. A daemon that is asleep, disconnecting or disconnected has no
    /// network whatever the last connectivity check says; an empty or unknown word
    /// reads as <see cref="NetworkConnectivity.Unknown"/>.
    /// </returns>
    public static NetworkConnectivity Parse(string? output)
    {
        var text = output?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return NetworkConnectivity.Unknown;
        }

        var fields = text.Split(':');
        var connectivity = fields[0].Trim().ToLowerInvariant();
        if (fields.Length > 1 && fields[1].Trim().ToLowerInvariant() is "asleep" or "disconnected" or "disconnecting")
        {
            return NetworkConnectivity.None;
        }

        return connectivity switch
        {
            "full" => NetworkConnectivity.Full,
            "limited" => NetworkConnectivity.Limited,
            "portal" => NetworkConnectivity.Portal,
            "none" => NetworkConnectivity.None,
            _ => NetworkConnectivity.Unknown,
        };
    }
}
