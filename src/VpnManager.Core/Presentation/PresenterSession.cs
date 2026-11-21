using VpnManager.Core.Services;

namespace VpnManager.Core.Presentation;

/// <summary>
/// Everything a presenter needs for one session: the operations. The application
/// drives one per-user session.
/// </summary>
/// <param name="Service">The operations for this session.</param>
public sealed record PresenterSession(TunnelService Service);
