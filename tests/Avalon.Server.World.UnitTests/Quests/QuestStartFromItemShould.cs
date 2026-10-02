using Avalon.Domain.Characters;
using Avalon.Network.Packets.Quest;
using Xunit;
using static Avalon.Server.World.UnitTests.Quests.QuestTestData;

namespace Avalon.Server.World.UnitTests.Quests;

/// <summary>An item starts a quest: the availability rules, no giver and no conversation.</summary>
public class QuestStartFromItemShould
{
    [Fact]
    public async Task Start_an_available_quest_anywhere()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        QuestClient c = w.Join();

        Assert.Equal(QuestResult.Ok, w.Quests.StartFromItem(c.Character, Hunt));

        Assert.Equal((CharacterQuestState.Active, 0), (c.Character.Quests.Get(Hunt)!.State, c.Character.Quests.Get(Hunt)!.Stage));
        Assert.Contains("Quest accepted: A Test Quest.", c.Character.Quests.PendingLines);
    }

    [Fact]
    public async Task Refuse_a_quest_that_is_not_available_or_unknown()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        QuestClient c = w.Join();

        Assert.Equal(QuestResult.NotAvailable, w.Quests.StartFromItem(c.Character, Tusks));   // needs Hunt done
        Assert.Equal(QuestResult.NotAvailable, w.Quests.StartFromItem(c.Character, 999u));
        Assert.Equal(QuestResult.Ok, w.Quests.StartFromItem(c.Character, Hunt));
        Assert.Equal(QuestResult.NotAvailable, w.Quests.StartFromItem(c.Character, Hunt));     // already held
        Assert.False(c.Character.Quests.IsActive(Tusks));
    }

    [Fact]
    public async Task Leave_accept_at_a_giver_unchanged()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        QuestClient c = w.Join();
        var giver = w.Place(Giver);
        w.Talk(c, giver);

        Assert.Equal(QuestResult.Ok, w.Quests.Accept(c.Connection, c.Character, Hunt, giver.Guid.RawValue));
        Assert.True(c.Character.Quests.IsActive(Hunt));
    }

    /// <summary>Kill and Talk objectives move; a Scripted one stays its own script's, a Collect one the bag's.</summary>
    [Fact]
    public async Task Advance_kill_and_talk_objectives_but_never_a_scripted_one()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        QuestClient c = w.Join(level: 2);
        QuestTestWorld.Complete(c, Hunt);
        QuestTestWorld.Complete(c, Tusks);
        Assert.Equal(QuestResult.Ok, w.Quests.StartFromItem(c.Character, Howl));

        Assert.True(w.Quests.AdvanceFromItem(c.Character, Howl, HowlKill, 1));   // stage 0 met: stage 1 starts
        Assert.True(w.Quests.AdvanceFromItem(c.Character, Howl, HowlTalk, 1));   // stage 1 met: stage 2 starts
        Assert.Equal(2, c.Character.Quests.Get(Howl)!.Stage);

        Assert.False(w.Quests.AdvanceFromItem(c.Character, Howl, HowlScripted, 1));
        Assert.Equal(0u, c.Character.Quests.Get(Howl)!.ProgressOf(HowlScripted));
    }

    [Fact]
    public async Task Never_advance_a_collect_objective_or_a_quest_not_held()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        QuestClient c = w.Join();
        QuestTestWorld.Complete(c, Hunt);
        Assert.Equal(QuestResult.Ok, w.Quests.StartFromItem(c.Character, Tusks));

        Assert.False(w.Quests.AdvanceFromItem(c.Character, Tusks, TusksCollect, 1));   // the bag counts it
        Assert.False(w.Quests.AdvanceFromItem(c.Character, Howl, HowlKill, 1));        // not held
        Assert.Equal(0u, c.Character.Quests.Get(Tusks)!.ProgressOf(TusksCollect));
    }
}
