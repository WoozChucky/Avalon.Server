using Avalon.LoadTest.Ramp;
using Xunit;

namespace Avalon.LoadTest.UnitTests;

public class GcStallShould
{
    [Fact]
    public void Judge_the_worst_average_pause_per_collection_within_the_window_and_tell_an_older_world_from_missing_data()
    {
        // The window starts at 100; the interval ending at 95 (one 40 ms collection) is not this window's. In the window:
        // four ordinary 5 ms collections (20 ms in all), then a 50 ms collection with two 5 ms ones (20 ms each on
        // average), then an interval with no collection, which has no average.
        CounterSeries[] pause = [Series("a", (85, 0.000), (95, 0.040), (105, 0.060), (115, 0.120), (125, 0.120))];
        CounterSeries[] collections =
        [
            Series("a", (85, 9), (95, 10), (105, 14), (115, 16), (125, 16)),
            Series("a", (85, 1), (95, 1), (105, 1), (115, 2), (125, 2)),
        ];

        Assert.Equal(new GcStall(GcStallReadout.Reported, 5), Rounded(GcStall.From(true, 1, Until(pause, 105), collections, 100)));
        Assert.Equal(new GcStall(GcStallReadout.Reported, 20), Rounded(GcStall.From(true, 1, pause, collections, 100)));
        // A window whose intervals had no collection has no stall: 0, judged.
        Assert.Equal(new GcStall(GcStallReadout.Reported, 0), GcStall.From(true, 1, pause, collections, 116));
        // A restarted process, its counters below the ones before them, added their whole values: 30 ms in one collection.
        // Each process's pause is divided by its own collections.
        CounterSeries[] restartedPause = [.. pause, Series("b", (95, 0.500), (105, 0.030))];
        CounterSeries[] restartedCollections = [.. collections, Series("b", (95, 100), (105, 1))];
        Assert.Equal(new GcStall(GcStallReadout.Reported, 30),
            Rounded(GcStall.From(true, 2, restartedPause, restartedCollections, 100)));

        // A world build without the pause time or the collections: it reports its ticks (the count's anchor) and not both.
        Assert.Equal(new GcStall(GcStallReadout.NotExported, null), GcStall.From(true, 0, [], [], 100));
        // Nothing from the world, a failed query, no interval in the window, or no collections of the pause's process
        // says nothing.
        Assert.Equal(GcStall.Unknown, GcStall.From(true, null, pause, collections, 100));
        Assert.Equal(GcStall.Unknown, GcStall.From(false, null, pause, collections, 100));
        Assert.Equal(GcStall.Unknown, GcStall.From(true, 1, null, collections, 100));
        Assert.Equal(GcStall.Unknown, GcStall.From(true, 1, pause, null, 100));
        Assert.Equal(GcStall.Unknown, GcStall.From(true, 1, pause, collections, 125));
        Assert.Equal(GcStall.Unknown, GcStall.From(true, 1, pause, [Series("b", (95, 1), (105, 2))], 100));
    }

    private static CounterSeries Series(string process, params (double Time, double Value)[] samples) =>
        new(process, [.. samples.Select(s => new CounterSample(s.Time, s.Value))]);

    private static CounterSeries[] Until(CounterSeries[] series, double time) =>
        [.. series.Select(s => s with { Samples = [.. s.Samples.Where(sample => sample.Time <= time)] })];

    private static GcStall Rounded(GcStall stall) => stall with { Ms = stall.Ms is { } ms ? Math.Round(ms, 6) : null };
}
