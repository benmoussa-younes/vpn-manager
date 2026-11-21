namespace VpnManager.Core.Runtime;

/// <summary>The resolver state of one link, as systemd-resolved reports it.</summary>
/// <param name="Servers">The DNS servers configured on the link.</param>
/// <param name="Domains">The routing domains; a full tunnel carries <c>~.</c>.</param>
/// <param name="DefaultRoute">Whether the link is the default route for DNS.</param>
public sealed record DnsState(IReadOnlyList<string> Servers, IReadOnlyList<string> Domains, bool DefaultRoute);
