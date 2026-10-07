using Avalon.Domain.Characters;
using Avalon.Network.Packets.Quest;
using Avalon.World.Quests;
using Avalon.World.Scripts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
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
        Assert.Equal(2, c.Character.Quests.PendingLines.Count(l => l == "A Test Quest: stage complete."));

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

    /// <summary>An item's start is an accept in every way but the giver: the accept hook, stage 0's start, the Accepted mark.</summary>
    [Fact]
    public async Task Run_the_accept_and_stage_0_hooks_and_mark_the_quest_accepted()
    {
        const uint Scripted = 7701, ScriptedKill = 77011;
        var recorder = new QuestScriptRecorder();
        IServiceProvider services = new ServiceCollection().AddSingleton<ILogger<SampleQuestScript>>(recorder).BuildServiceProvider();
        IScriptManager scripts = Substitute.For<IScriptManager>();
        scripts.GetQuestScript(nameof(SampleQuestScript)).Returns(typeof(SampleQuestScript));
        QuestTestWorld w = await QuestTestWorld.CreateAsync(
            [Quest(Scripted, script: nameof(SampleQuestScript)).WithStage(0, Kill(ScriptedKill, Boar, 1))],
            scripts: scripts, services: services);
        QuestClient c = w.Join();

        Assert.Equal(QuestResult.Ok, w.Quests.StartFromItem(c.Character, Scripted));

        Assert.Equal(["OnAccepted", "OnStageStarted:0"], recorder.Calls.Where(n => n != "CanAccept"));
        Assert.Equal(QuestClientChange.Accepted, c.Character.Quests.ClientChanges[Scripted]);
    }

    /// <summary>AddProgress's rules hold for an item too: no amount of 0, no ready quest, no objective of a later stage.</summary>
    [Fact]
    public async Task Refuse_an_amount_of_0_a_ready_quest_and_an_objective_of_a_later_stage()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        QuestClient c = w.Join(level: 2);
        QuestTestWorld.Complete(c, Hunt);
        QuestTestWorld.Complete(c, Tusks);
        Assert.Equal(QuestResult.Ok, w.Quests.StartFromItem(c.Character, Howl));

        Assert.False(w.Quests.AdvanceFromItem(c.Character, Howl, HowlKill, 0));
        Assert.False(w.Quests.AdvanceFromItem(c.Character, Howl, HowlTalk, 1));   // stage 1's, and stage 0 runs
        Assert.Equal((0, 0u, 0u), (c.Character.Quests.Get(Howl)!.Stage,
            c.Character.Quests.Get(Howl)!.ProgressOf(HowlKill), c.Character.Quests.Get(Howl)!.ProgressOf(HowlTalk)));

        QuestClient ready = w.Join(id: 2);
        Assert.Equal(QuestResult.Ok, w.Quests.StartFromItem(ready.Character, Hunt));
        Assert.True(w.Quests.AdvanceFromItem(ready.Character, Hunt, HuntKill, 2));
        Assert.Equal(CharacterQuestState.ReadyToTurnIn, ready.Character.Quests.Get(Hunt)!.State);
        Assert.False(w.Quests.AdvanceFromItem(ready.Character, Hunt, HuntKill, 1));
    }
}
