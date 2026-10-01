using Avalon.Domain.Characters;
using Xunit;
using static Avalon.Server.World.UnitTests.Quests.QuestTestData;

namespace Avalon.Server.World.UnitTests.Quests;

/// <summary>Progress counts only in the current stage and is capped; a complete stage starts the next; the last makes the quest ready.</summary>
public class QuestStagesShould
{
    private static async Task<(QuestTestWorld W, QuestClient C)> HowlAsync()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        QuestClient c = w.Join(level: 2);
        QuestTestWorld.Complete(c, Hunt);
        QuestTestWorld.Complete(c, Tusks);
        w.Accept(c, Howl);
        return (w, c);
    }

    [Fact]
    public async Task Walk_the_stages_in_order_and_become_ready()
    {
        (QuestTestWorld w, QuestClient c) = await HowlAsync();

        Assert.True(w.Quests.AddProgress(c.Character, Howl, HowlKill, 1));
        Assert.Equal(1, c.Character.Quests.Get(Howl)!.Stage);

        Assert.True(w.Quests.AddProgress(c.Character, Howl, HowlTalk, 1));
        Assert.Equal(2, c.Character.Quests.Get(Howl)!.Stage);

        Assert.True(w.Quests.AddProgress(c.Character, Howl, HowlScripted, 1));
        Assert.Equal(CharacterQuestState.ReadyToTurnIn, c.Character.Quests.Get(Howl)!.State);
        Assert.Equal(2, c.Character.Quests.Get(Howl)!.Stage);
        Assert.Equal(
            ["Things done: 1/1", "A Test Quest: stage complete.", "Things done: 1/1", "A Test Quest: stage complete.",
             "Things done: 1/1", "A Test Quest: ready to turn in."],
            c.Character.Quests.PendingLines.SkipWhile(l => l.StartsWith("Quest accepted", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Ignore_progress_for_an_objective_outside_the_current_stage()
    {
        (QuestTestWorld w, QuestClient c) = await HowlAsync();

        Assert.False(w.Quests.AddProgress(c.Character, Howl, HowlTalk, 1));
        Assert.Equal(0u, c.Character.Quests.Get(Howl)!.ProgressOf(HowlTalk));
    }

    [Fact]
    public async Task Cap_progress_at_the_count()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        QuestClient c = w.Join();
        w.Accept(c, Hunt);

        Assert.True(w.Quests.AddProgress(c.Character, Hunt, HuntKill, 1));
        Assert.True(w.Quests.AddProgress(c.Character, Hunt, HuntKill, uint.MaxValue));

        Assert.Equal(2u, c.Character.Quests.Get(Hunt)!.ProgressOf(HuntKill));
        Assert.False(w.Quests.AddProgress(c.Character, Hunt, HuntKill, 1));   // ready: nothing more counts
    }

    [Fact]
    public async Task Count_nothing_for_zero()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        QuestClient c = w.Join();
        w.Accept(c, Hunt);

        Assert.False(w.Quests.AddProgress(c.Character, Hunt, HuntKill, 0));
    }
}
