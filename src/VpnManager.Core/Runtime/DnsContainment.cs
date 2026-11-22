namespace VpnManager.Core.Runtime;

/// <summary>How a link resolves names, from the routing domains it carries.</summary>
public enum DnsContainment
{
    /// <summary>Every name resolves through the tunnel (<c>~.</c> or the default route).</summary>
    Contained,

    /// <summary>Only the listed domains resolve through the tunnel (split DNS).</summary>
    Split,

    /// <summary>No routing domain: the physical resolver may still answer.</summary>
    Uncontained,

    /// <summary>The resolver state could not be read.</summary>
    Unknown,
}
