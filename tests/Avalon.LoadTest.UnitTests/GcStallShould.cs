using Avalon.LoadTest.Ramp;
using Xunit;

namespace Avalon.LoadTest.UnitTests;

public class GcStallShould
{
    [Fact]
    public void Judge_the_worst_increase_between_samples_within_the_window_and_tell_an_older_world_from_missing_data()
    {
        // The window starts at 100. The sample at 95 is before it and only anchors the one at 105; the 40 ms that
        // landed by 95 are not this window's.
        CounterSample[] world = [new(85, 0.000), new(95, 0.040), new(105, 0.045), new(115, 0.065), new(125, 0.070)];

        Assert.Equal(new GcStall(GcStallReadout.Reported, 20), Rounded(GcStall.From(true, 1, [world], 100)));
        // The worst of several series; a sample below the one before it is a restarted process whose count began at 0.
        CounterSample[] restarted = [new(95, 0.500), new(105, 0.030), new(115, 0.031)];
        Assert.Equal(new GcStall(GcStallReadout.Reported, 30), Rounded(GcStall.From(true, 2, [world, restarted], 100)));
        // No pause added in the window is a stall of 0, judged.
        Assert.Equal(new GcStall(GcStallReadout.Reported, 0),
            GcStall.From(true, 1, [[new(95, 0.3), new(105, 0.3), new(115, 0.3)]], 100));

        // A world build without the pause time: it reports its ticks (the count's anchor) and no pause time.
        Assert.Equal(new GcStall(GcStallReadout.NotExported, null), GcStall.From(true, 0, [], 100));
        // Nothing from the world, a failed query, or no sample in the window with one before it says nothing.
        Assert.Equal(GcStall.Unknown, GcStall.From(true, null, [world], 100));
        Assert.Equal(GcStall.Unknown, GcStall.From(false, null, [world], 100));
        Assert.Equal(GcStall.Unknown, GcStall.From(true, 1, null, 100));
        Assert.Equal(GcStall.Unknown, GcStall.From(true, 1, [[new(105, 0.045)]], 100));
        Assert.Equal(GcStall.Unknown, GcStall.From(true, 1, [world], 125));
    }

    private static GcStall Rounded(GcStall stall) => stall with { Ms = stall.Ms is { } ms ? Math.Round(ms, 6) : null };
}
