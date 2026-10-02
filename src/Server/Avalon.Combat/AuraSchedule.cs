namespace Avalon.Combat;

/// <summary>
/// When an aura ends and when it still ticks: <see cref="TicksLeft" /> ticks at <c>ExpiresAt - j x Interval</c> for
/// j = TicksLeft - 1 down to 0, so the last lands at expiry. Absolute times on the instance's clock: a late update owes
/// every tick whose time has passed, and never more than are left.
/// </summary>
public readonly record struct AuraSchedule(DateTimeOffset ExpiresAt, TimeSpan Interval, int TicksLeft)
{
    public static AuraSchedule Start(DateTimeOffset now, uint durationMs, uint tickIntervalMs) =>
        new(now + TimeSpan.FromMilliseconds(durationMs), TimeSpan.FromMilliseconds(tickIntervalMs),
            AuraRules.TickCount(durationMs, tickIntervalMs));

    /// <summary>
    /// A saved aura resumed <paramref name="remainingMs" /> before its end, its time having stood still while it was not
    /// in the world. It is never owed more ticks than its remaining time holds.
    /// </summary>
    public static AuraSchedule Resume(DateTimeOffset now, uint remainingMs, uint tickIntervalMs, int ticksLeft)
    {
        int fits = tickIntervalMs == 0 ? 0 : (int)((remainingMs + (long)tickIntervalMs - 1) / tickIntervalMs);
        return new(now + TimeSpan.FromMilliseconds(remainingMs), TimeSpan.FromMilliseconds(tickIntervalMs),
            Math.Clamp(ticksLeft, 0, fits));
    }

    /// <summary>How many ticks are owed at <paramref name="now" />: those whose time has come, at most <see cref="TicksLeft" />.</summary>
    public int Due(DateTimeOffset now)
    {
        if (TicksLeft <= 0) return 0;

        long left = (ExpiresAt - now).Ticks;
        if (left <= 0 || Interval.Ticks <= 0) return TicksLeft;

        // The ticks still to come are those with j x Interval >= left.
        long notYet = (left + Interval.Ticks - 1) / Interval.Ticks;
        return (int)Math.Max(0L, TicksLeft - notYet);
    }

    public AuraSchedule AfterTicks(int count) => this with { TicksLeft = Math.Max(0, TicksLeft - count) };

    public bool Expired(DateTimeOffset now) => now >= ExpiresAt;

    public TimeSpan Remaining(DateTimeOffset now) => ExpiresAt > now ? ExpiresAt - now : TimeSpan.Zero;
}
