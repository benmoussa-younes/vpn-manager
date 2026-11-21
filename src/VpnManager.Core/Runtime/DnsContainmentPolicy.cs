namespace VpnManager.Core.Runtime;

/// <summary>
/// Classifies a link's resolver state and words it for the operator. It never claims
/// a leak is impossible: it reports where the link resolves names, and an uncontained
/// link is a warning, not a proof of leak.
/// </summary>
public static class DnsContainmentPolicy
{
    /// <summary>Classifies the state.</summary>
    /// <param name="state">The resolver state.</param>
    /// <returns>The containment.</returns>
    public static DnsContainment Classify(DnsState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.DefaultRoute || state.Domains.Any(domain => domain is "~." or "."))
        {
            return DnsContainment.Contained;
        }

        return state.Domains.Count > 0 ? DnsContainment.Split : DnsContainment.Uncontained;
    }

    /// <summary>Words the containment.</summary>
    /// <param name="containment">The containment.</param>
    /// <returns>A short sentence.</returns>
    public static string Describe(DnsContainment containment) => containment switch
    {
        DnsContainment.Contained => "every name resolves through the tunnel",
        DnsContainment.Split => "only the tunnel's domains resolve through it",
        DnsContainment.Uncontained => "no routing domain; the physical resolver may still answer",
        _ => "unknown",
    };
}
