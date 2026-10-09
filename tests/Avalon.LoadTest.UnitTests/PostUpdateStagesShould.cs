using Avalon.LoadTest.Ramp;
using Xunit;

namespace Avalon.LoadTest.UnitTests;

public class PostUpdateStagesShould
{
    [Fact]
    public void Tell_an_older_world_from_missing_data_and_keep_the_tick_order()
    {
        var mean = new Dictionary<string, double> { ["outbox"] = 3900, ["quests"] = 40, ["custom"] = 5 };
        var p99 = new Dictionary<string, double> { ["outbox"] = 9800, ["quests"] = double.NaN, ["inventory"] = 12 };

        // A world build from before the histogram: it reports its ticks (the count's anchor) and no stage.
        Assert.Equal(PostUpdateReadout.NotExported, PostUpdateStages.From(true, 0, mean, p99).Readout);
        // Nothing from the world at all (a stalled export, a scrape gap), or a failed query, says nothing about its build.
        Assert.Equal(PostUpdateReadout.Unknown, PostUpdateStages.From(true, null, mean, p99).Readout);
        Assert.Equal(PostUpdateReadout.Unknown, PostUpdateStages.From(false, null, mean, p99).Readout);
        Assert.Equal(PostUpdateReadout.Unknown, PostUpdateStages.From(true, 9, null, null).Readout);

        // One stage query failing leaves its own column empty, not the row.
        var meanOnly = PostUpdateStages.From(true, 9, mean, null);
        Assert.Equal(PostUpdateReadout.Reported, meanOnly.Readout);
        Assert.All(meanOnly.Stages, stage => Assert.Null(stage.P99Us));

        var read = PostUpdateStages.From(true, 9, mean, p99);
        Assert.Equal(PostUpdateReadout.Reported, read.Readout);
        Assert.Equal(
            [
                new StageTiming("quests", 40, null),
                new StageTiming("inventory", null, 12),
                new StageTiming("outbox", 3900, 9800),
                new StageTiming("custom", 5, null),
            ],
            read.Stages);
    }
}
