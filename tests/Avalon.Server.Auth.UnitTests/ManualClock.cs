namespace Avalon.Server.Auth.UnitTests;

/// <summary>
/// A <see cref="TimeProvider"/> that moves only when told to. Its timestamps follow the same
/// clock (one tick per 100 ns), so code that measures elapsed time by timestamp moves with it too.
/// </summary>
internal sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public void Advance(TimeSpan by) => _now += by;

    public override DateTimeOffset GetUtcNow() => _now;

    public override long GetTimestamp() => _now.UtcTicks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
}
