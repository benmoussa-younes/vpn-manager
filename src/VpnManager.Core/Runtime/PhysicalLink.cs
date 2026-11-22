namespace VpnManager.Core.Runtime;

/// <summary>
/// The physical default link: the interface and gateway the kernel would use when no
/// tunnel is in the way. A change means the machine roamed, so a full tunnel must
/// re-resolve its endpoint.
/// </summary>
/// <param name="Interface">The default-route interface, for example <c>wlan0</c>.</param>
/// <param name="Gateway">The default gateway address, for example <c>192.168.1.1</c>.</param>
public sealed record PhysicalLink(string Interface, string Gateway)
{
    /// <summary>Gets a stable key for the link, used for change detection.</summary>
    public string Key => string.Concat(Interface, "/", Gateway);
}
