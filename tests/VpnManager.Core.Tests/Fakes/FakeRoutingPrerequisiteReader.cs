using VpnManager.Core.Runtime;

namespace VpnManager.Core.Tests.Fakes;

/// <summary>A full-tunnel prerequisite reader whose value the test chooses.</summary>
internal sealed class FakeRoutingPrerequisiteReader : IRoutingPrerequisiteReader
{
    /// <summary>The value returned, or <see langword="null"/> for unknown.</summary>
    internal RoutingPrerequisite? Value { get; set; }

    public RoutingPrerequisite? Read() => Value;
}
