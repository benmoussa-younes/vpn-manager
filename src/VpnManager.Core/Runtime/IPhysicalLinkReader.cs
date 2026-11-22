namespace VpnManager.Core.Runtime;

/// <summary>
/// Reads the current physical default link, or nothing when the machine has none.
/// It is the rootless signal a full tunnel uses to notice that the network moved.
/// </summary>
public interface IPhysicalLinkReader
{
    /// <summary>Reads the physical default link.</summary>
    /// <returns>The link, or <see langword="null"/> when there is no usable default route.</returns>
    PhysicalLink? Read();
}
