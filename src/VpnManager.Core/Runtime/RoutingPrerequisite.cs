namespace VpnManager.Core.Runtime;

/// <summary>
/// The kernel settings a full tunnel needs: with a strict reverse path filter, the
/// peer's marked replies are dropped unless the kernel validates the source against
/// the packet mark. NetworkManager does not set this; the package does.
/// </summary>
/// <param name="SrcValidMark">The value of <c>net.ipv4.conf.all.src_valid_mark</c>.</param>
/// <param name="RpFilter">The value of <c>net.ipv4.conf.all.rp_filter</c>.</param>
public sealed record RoutingPrerequisite(int SrcValidMark, int RpFilter)
{
    /// <summary>Gets whether the machine can carry a full tunnel to a remote endpoint.</summary>
    public bool IsSatisfied => RpFilter == 0 || SrcValidMark == 1;

    /// <summary>Gets the exact remediation for an unsatisfied machine.</summary>
    public string Remediation => IsSatisfied
        ? "the machine is ready for a full tunnel"
        : "install the VPN Manager package, or set net.ipv4.conf.all.src_valid_mark=1";
}
