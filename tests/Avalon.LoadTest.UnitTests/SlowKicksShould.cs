using Avalon.LoadTest.Ramp;
using Xunit;

namespace Avalon.LoadTest.UnitTests;

public class SlowKicksShould
{
    [Fact]
    public void Count_the_kicks_within_the_window_and_leave_an_older_world_unjudged()
    {
        // The window starts at 100. The bytes series rose by 1 before the window (not this window's) and by 2 within it;
        // the stall series was born within the window, so its first sample counts whole (1) and then rises by 1; a
        // restarted process's series fell below the sample before it and counts its whole value (2).
        CounterSeries[] kicks =
        [
            Series("reason=bytes", (40, 2), (95, 3), (105, 3), (115, 5)),
            Series("reason=stall", (110, 1), (120, 2)),
            Series("instance=b,reason=bytes", (95, 7), (105, 2)),
        ];
        Assert.Equal(new SlowKicks(SendReadout.Reported, 6), SlowKicks.From(true, 1, kicks, 100));
        // A world that exports its send passes and has no kick series has kicked no one: 0, judged.
        Assert.Equal(new SlowKicks(SendReadout.Reported, 0), SlowKicks.From(true, 1, [], 100));

        // A world build from before the send threads reports its ticks and no send pass: the step is not judged on slow
        // kicks, which neither read 0 nor make the step unknown.
        var older = SlowKicks.From(true, 0, [], 100);
        Assert.Equal(new SlowKicks(SendReadout.NotExported, null), older);
        var server = new ServerValues(5, 60, 0, 0.3, 100, 0, 0, 0, 1) { SlowKicks = older };
        Assert.Contains(LimitName.SlowKicks, RampRunner.NotJudged(server));

        // Nothing from the world or a failed query says nothing.
        Assert.Equal(SlowKicks.Unknown, SlowKicks.From(true, null, kicks, 100));
        Assert.Equal(SlowKicks.Unknown, SlowKicks.From(false, null, kicks, 100));
        Assert.Equal(SlowKicks.Unknown, SlowKicks.From(true, 1, null, 100));
        Assert.DoesNotContain(LimitName.SlowKicks, RampRunner.NotJudged(server with { SlowKicks = SlowKicks.Unknown }));
    }

    private static CounterSeries Series(string process, params (double Time, double Value)[] samples) =>
        new(process, [.. samples.Select(s => new CounterSample(s.Time, s.Value))]);
}
