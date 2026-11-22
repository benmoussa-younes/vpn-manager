namespace VpnManager.Core.Runtime;

/// <summary>
/// Reads the kernel settings a full tunnel needs, or nothing when they cannot be
/// read. It is read-only and rootless: the application can only detect and warn.
/// </summary>
public interface IRoutingPrerequisiteReader
{
    /// <summary>Reads the settings.</summary>
    /// <returns>The settings, or <see langword="null"/> when they cannot be read.</returns>
    RoutingPrerequisite? Read();
}
