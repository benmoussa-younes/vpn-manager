using VpnManager.Core.Runtime;

namespace VpnManager.Core.Tests.Fakes;

/// <summary>A physical-link reader whose observation the test chooses.</summary>
internal sealed class FakePhysicalLinkReader : IPhysicalLinkReader
{
    /// <summary>The link the next read returns, or <see langword="null"/> for none.</summary>
    internal PhysicalLink? Link { get; set; }

    /// <summary>How many times the link was read.</summary>
    internal int Reads { get; private set; }

    public PhysicalLink? Read()
    {
        Reads++;
        return Link;
    }
}
