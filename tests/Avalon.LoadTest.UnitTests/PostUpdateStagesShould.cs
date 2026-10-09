using Avalon.LoadTest.Ramp;
using Xunit;

namespace Avalon.LoadTest.UnitTests;

public class PostUpdateStagesShould
{
    [Fact]
    public void Tell_an_older_world_and_a_failed_query_apart_and_keep_the_tick_order()
    {
        var mean = new Dictionary<string, double> { ["outbox"] = 3900, ["quests"] = 40, ["custom"] = 5 };
        var p99 = new Dictionary<string, double> { ["outbox"] = 9800, ["quests"] = double.NaN, ["inventory"] = 12 };

        // A world build from before the histogram: answered, and no series at all.
        Assert.Equal(PostUpdateReadout.NotExported, PostUpdateStages.From(true, null, mean, p99).Readout);
        // A query that failed says nothing about the world.
        Assert.Equal(PostUpdateReadout.Unknown, PostUpdateStages.From(false, null, mean, p99).Readout);
        Assert.Equal(PostUpdateReadout.Unknown, PostUpdateStages.From(true, 9, mean, null).Readout);

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
