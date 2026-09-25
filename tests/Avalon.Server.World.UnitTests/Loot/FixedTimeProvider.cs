namespace Avalon.Server.World.UnitTests.Loot;

/// <summary>A clock that says what the test tells it to.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
