namespace Avalon.Server.World.UnitTests;

/// <summary>A fixed clock that throws from <see cref="GetUtcNow" /> once <see cref="Broken" /> is set: a way to make whatever reads it throw.</summary>
internal sealed class BreakableClock : TimeProvider
{
    private readonly DateTimeOffset _now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    public bool Broken { get; set; }

    public override DateTimeOffset GetUtcNow() => Broken ? throw new InvalidOperationException("clock broken") : _now;
}
