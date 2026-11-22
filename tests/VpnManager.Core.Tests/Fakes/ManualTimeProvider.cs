namespace VpnManager.Core.Tests.Fakes;

/// <summary>A clock the test advances by hand, so a transfer rate is deterministic.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    internal void Advance(TimeSpan delta) => _now += delta;
}
