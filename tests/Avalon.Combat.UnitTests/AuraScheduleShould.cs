namespace Avalon.Combat.UnitTests;

/// <summary>Ticks anchored at expiry: n ticks at expiry - j x interval, the last at expiry, caught up by absolute time.</summary>
public class AuraScheduleShould
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Owe_nothing_before_the_first_tick_and_one_on_it()
    {
        var s = AuraSchedule.Start(T0, 12000, 3000);

        Assert.Equal((T0.AddSeconds(12), TimeSpan.FromSeconds(3), 4), (s.ExpiresAt, s.Interval, s.TicksLeft));
        Assert.Equal(0, s.Due(T0.AddMilliseconds(2999)));
        Assert.Equal(1, s.Due(T0.AddSeconds(3)));
    }

    [Fact]
    public void Owe_every_tick_a_stall_skipped_and_never_more_than_are_left()
    {
        var s = AuraSchedule.Start(T0, 12000, 3000);

        Assert.Equal(3, s.Due(T0.AddSeconds(9.5)));
        Assert.Equal(4, s.Due(T0.AddSeconds(60)));
        Assert.Equal(1, s.AfterTicks(3).Due(T0.AddSeconds(60)));
        Assert.Equal(0, s.AfterTicks(4).Due(T0.AddSeconds(60)));
    }

    /// <summary>10 s at 3 s: three ticks, at 4, 7 and 10 s, so the last lands at expiry.</summary>
    [Fact]
    public void Land_the_last_tick_at_expiry_when_the_interval_does_not_divide_the_duration()
    {
        var s = AuraSchedule.Start(T0, 10000, 3000);

        Assert.Equal(0, s.Due(T0.AddSeconds(3.9)));
        Assert.Equal(1, s.Due(T0.AddSeconds(4)));
        Assert.Equal(3, s.Due(T0.AddSeconds(10)));
        Assert.True(s.Expired(T0.AddSeconds(10)));
        Assert.False(s.Expired(T0.AddSeconds(9.999)));
    }

    [Fact]
    public void Resume_with_the_time_and_ticks_left_and_never_owe_more_than_the_time_allows()
    {
        var s = AuraSchedule.Resume(T0, 4500, 3000, ticksLeft: 4);

        Assert.Equal((T0.AddMilliseconds(4500), 2), (s.ExpiresAt, s.TicksLeft));   // ticks at 1.5 s and 4.5 s
        Assert.Equal(1, s.Due(T0.AddSeconds(1.5)));
        Assert.Equal(TimeSpan.FromMilliseconds(4500), s.Remaining(T0));
        Assert.Equal(TimeSpan.Zero, s.Remaining(T0.AddSeconds(5)));
    }

    /// <summary>6 s left at 3 s: ticks at 0, 3 and 6 s, the one at the resume instant owed at once, as Due owes it.</summary>
    [Fact]
    public void Keep_a_tick_due_at_the_resume_instant()
    {
        var s = AuraSchedule.Resume(T0, 6000, 3000, ticksLeft: 4);

        Assert.Equal(3, s.TicksLeft);
        Assert.Equal(1, s.Due(T0));
        Assert.Equal(3, s.Due(T0.AddSeconds(6)));
    }

    [Fact]
    public void Keep_the_final_tick_of_an_aura_resumed_at_its_end()
    {
        var s = AuraSchedule.Resume(T0, 0, 3000, ticksLeft: 2);

        Assert.Equal(1, s.TicksLeft);
        Assert.Equal(1, s.Due(T0));
        Assert.True(s.Expired(T0));
    }

    [Fact]
    public void Owe_no_ticks_for_an_aura_that_has_none()
    {
        var s = AuraSchedule.Start(T0, 6000, 0);

        Assert.Equal(0, s.TicksLeft);
        Assert.Equal(0, s.Due(T0.AddSeconds(7)));
        Assert.True(s.Expired(T0.AddSeconds(6)));
    }
}
