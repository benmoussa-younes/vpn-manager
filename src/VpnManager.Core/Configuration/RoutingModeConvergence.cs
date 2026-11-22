using Core.Results;
using VpnManager.Core.Model;

namespace VpnManager.Core.Configuration;

/// <summary>
/// The pure full/split convergence. It reproduces, as a value transformation, what
/// the workstation's <c>ensure_wg_mode_directives</c> does to a wg-quick file:
/// full pins the default route and wg-quick's fwmark, split removes both and keeps
/// the mesh list. The first peer is the managed one; every other peer is preserved.
/// </summary>
/// <remarks>
/// The values are the ones the deployment uses: the default route is
/// <c>0.0.0.0/0, ::/0</c>, the table is <c>auto</c> and the fwmark is 51820 (the
/// value wg-quick derives for a default route; <c>0xca6c</c> in hex).
/// </remarks>
public static class RoutingModeConvergence
{
    /// <summary>The routing table value a full tunnel requires.</summary>
    public const string FullTable = "auto";

    /// <summary>The fwmark a full tunnel requires, as wg-quick's default-route value.</summary>
    public const int FullFwMarkValue = 51820;

    /// <summary>The two prefixes that make a peer carry the default route.</summary>
    public static IReadOnlyList<string> FullAllowedIPs { get; } = ["0.0.0.0/0", "::/0"];

    /// <summary>Decides the mode of a configuration from its first peer's allowed IPs.</summary>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The detected mode.</returns>
    public static TunnelRoutingMode Detect(TunnelConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var first = configuration.Peers.Count > 0 ? configuration.Peers[0] : null;
        return first is not null && HasDefaultRoute(first.AllowedIPs)
            ? TunnelRoutingMode.Full
            : TunnelRoutingMode.Split;
    }

    /// <summary>
    /// Applies a mode to a configuration. Switching to split without a mesh list
    /// while the tunnel still carries the default route is refused, exactly as the
    /// deployment refuses to leave the full route under a split guard.
    /// </summary>
    /// <param name="configuration">The configuration to converge.</param>
    /// <param name="mode">The mode to apply.</param>
    /// <param name="splitAllowedIps">The mesh prefixes for split mode, when known.</param>
    /// <returns>The converged configuration, or the refusal.</returns>
    public static Result<TunnelConfiguration> Apply(
        TunnelConfiguration configuration,
        TunnelRoutingMode mode,
        IReadOnlyList<string>? splitAllowedIps = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var peers = configuration.Peers.ToArray();

        if (mode == TunnelRoutingMode.Full)
        {
            var settings = configuration.Interface with
            {
                Table = FullTable,
                FwMark = FullFwMarkValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };

            if (peers.Length > 0)
            {
                peers[0] = peers[0] with { AllowedIPs = FullAllowedIPs };
            }

            return Result<TunnelConfiguration>.Success(configuration with { Interface = settings, Peers = peers });
        }

        var first = peers.Length > 0 ? peers[0] : null;
        var mesh = Normalize(splitAllowedIps);

        if (mesh.Length == 0 && first is not null && HasDefaultRoute(first.AllowedIPs))
        {
            return Result<TunnelConfiguration>.Failure(WireGuardErrors.Validation(
                WireGuardErrorCodes.RoutingModeSplitMissingAllowedIps,
                "Switching to split needs the mesh AllowedIPs to replace the default route; the tunnel still routes everything."));
        }

        var splitSettings = configuration.Interface with { Table = null, FwMark = null };
        if (first is not null && mesh.Length > 0)
        {
            peers[0] = first with { AllowedIPs = mesh };
        }

        return Result<TunnelConfiguration>.Success(configuration with { Interface = splitSettings, Peers = peers });
    }

    private static bool HasDefaultRoute(IReadOnlyList<string> allowedIPs) =>
        allowedIPs
            .SelectMany(Split)
            .Any(token => token.EndsWith("/0", StringComparison.Ordinal));

    private static string[] Normalize(IReadOnlyList<string>? values) =>
        values is null
            ? []
            : values.SelectMany(Split).ToArray();

    private static IEnumerable<string> Split(string value) =>
        value.Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
