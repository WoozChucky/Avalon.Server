using Avalon.LoadTest.Ramp;
using Xunit;

namespace Avalon.LoadTest.UnitTests;

public class SlowKicksShould
{
    /// <summary>The step starts at 70, its window runs from 100 to 160, and the read sees samples up to 175.</summary>
    private static readonly KickSpan s_span = new(StepStart: 70, WindowStart: 100, WindowEnd: 160, ReadHorizon: 175);

    [Fact]
    public void Count_the_kicks_within_the_window_and_leave_an_older_world_unjudged()
    {
        // The bytes series rose by 1 before the window (not this window's), by 2 within it, and by 1 in the interval
        // closing after the window's end, read before the horizon; it then rose by 2 in an interval wholly after the
        // window and by 1 after the horizon, neither of them this window's. The stall series was born within the window,
        // so its first sample counts whole (1) and then rises by 1; a restarted process's series fell below the sample
        // before it and counts its whole value (2). A series whose sample before the window predates the step's start
        // and added nothing since is no kick.
        CounterSeries[] kicks =
        [
            Series("reason=bytes", (40, 2), (95, 3), (105, 3), (115, 5), (155, 5), (165, 6), (172, 8), (185, 9)),
            Series("reason=stall", (110, 1), (120, 2)),
            Series("instance=b,reason=bytes", (95, 7), (105, 2)),
            Series("instance=c,reason=stall", (60, 4), (105, 4)),
        ];
        Assert.Equal(new SlowKicks(SendReadout.Reported, 7), SlowKicks.From(true, 1, kicks, s_span));
        // A world that exports its send passes and has no kick series has kicked no one: 0, judged.
        Assert.Equal(new SlowKicks(SendReadout.Reported, 0), SlowKicks.From(true, 1, [], s_span));
        // Kicks added over an export gap reaching back before the step's start cannot be placed in this step.
        Assert.Equal(SlowKicks.Unknown, SlowKicks.From(true, 1, [.. kicks, Series("instance=d,reason=bytes", (60, 1), (105, 2))], s_span));

        // A world build from before the send threads reports its ticks and no send pass: the step is not judged on slow
        // kicks, which neither read 0 nor make the step unknown.
        var older = SlowKicks.From(true, 0, [], s_span);
        Assert.Equal(new SlowKicks(SendReadout.NotExported, null), older);
        var server = new ServerValues(5, 60, 0, 0.3, 100, 0, 0, 0, 1) { SlowKicks = older };
        Assert.Contains(LimitName.SlowKicks, RampRunner.NotJudged(server));

        // Nothing from the world or a failed query says nothing.
        Assert.Equal(SlowKicks.Unknown, SlowKicks.From(true, null, kicks, s_span));
        Assert.Equal(SlowKicks.Unknown, SlowKicks.From(false, null, kicks, s_span));
        Assert.Equal(SlowKicks.Unknown, SlowKicks.From(true, 1, null, s_span));
        Assert.DoesNotContain(LimitName.SlowKicks, RampRunner.NotJudged(server with { SlowKicks = SlowKicks.Unknown }));
    }

    private static CounterSeries Series(string process, params (double Time, double Value)[] samples) =>
        new(process, [.. samples.Select(s => new CounterSample(s.Time, s.Value))]);
}
