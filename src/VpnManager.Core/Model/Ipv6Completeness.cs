namespace VpnManager.Core.Model;

/// <summary>How complete a profile's IPv6 is.</summary>
public enum Ipv6Completeness
{
    /// <summary>The profile carries no IPv6 at all.</summary>
    None,

    /// <summary>The profile declares an IPv6 address and routes <c>::/0</c>.</summary>
    Complete,

    /// <summary>The profile declares IPv6 but not as a full path.</summary>
    Partial,
}
