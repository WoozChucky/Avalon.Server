using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.World.Scripts;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;
using static Avalon.Server.World.UnitTests.Quests.QuestTestData;

namespace Avalon.Server.World.UnitTests.Quests;

/// <summary>Progress counts only in the current stage and is capped; a complete stage starts the next; the last makes the quest ready.</summary>
public class QuestStagesShould
{
    private static async Task<(QuestTestWorld W, QuestClient C)> HowlAsync(QuestScriptRecorder? recorder = null)
    {
        QuestTestWorld w;
        if (recorder is null)
            w = await QuestTestWorld.CreateAsync();
        else
        {
            // Howl runs the sample script, so a test can drive progress from inside its real stage-start hook.
            List<QuestTemplate> quests = Chain();
            quests.Single(q => q.Id.Value == Howl).ScriptName = nameof(SampleQuestScript);
            var scripts = Substitute.For<IScriptManager>();
            scripts.GetQuestScript(nameof(SampleQuestScript)).Returns(typeof(SampleQuestScript));
            w = await QuestTestWorld.CreateAsync(quests, scripts: scripts,
                services: new ServiceCollection().AddSingleton<Microsoft.Extensions.Logging.ILogger<SampleQuestScript>>(recorder).BuildServiceProvider());
        }

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
    /// <summary>
    /// The settle guard is per quest: progress a stage start gives another quest (of this character or another)
    /// settles that quest at once, rather than being skipped and left Active with every objective met.
    /// </summary>
    [Fact]
    public async Task Settle_another_quest_advanced_from_inside_a_stage_start()
    {
        var recorder = new QuestScriptRecorder();
        (QuestTestWorld w, QuestClient c) = await HowlAsync(recorder);
        QuestClient other = w.Join(id: 2);
        other.Character.Quests.Start(Hunt, DateTime.UnixEpoch);
        c.Character.Quests.Start(Hunt, DateTime.UnixEpoch);
        recorder.OnStage = (context, stage) =>
        {
            if (context.QuestId == Howl && stage == 1)
            {
                w.Quests.AddProgress(c.Character, Hunt, HuntKill, 2);
                w.Quests.AddProgress(other.Character, Hunt, HuntKill, 2);
            }
        };

        Assert.True(w.Quests.AddProgress(c.Character, Howl, HowlKill, 1));

        Assert.Equal(1, c.Character.Quests.Get(Howl)!.Stage);
        Assert.Equal(CharacterQuestState.ReadyToTurnIn, c.Character.Quests.Get(Hunt)!.State);
        Assert.Equal(CharacterQuestState.ReadyToTurnIn, other.Character.Quests.Get(Hunt)!.State);
    }

    /// <summary>A stage start that meets its own stage's objectives: the outer loop goes on to the next stage.</summary>
    [Fact]
    public async Task Walk_on_when_a_stage_start_meets_its_own_objectives()
    {
        var recorder = new QuestScriptRecorder();
        (QuestTestWorld w, QuestClient c) = await HowlAsync(recorder);
        recorder.OnStage = (context, stage) =>
        {
            if (context.QuestId == Howl && stage == 1)
                w.Quests.AddProgress(c.Character, Howl, HowlTalk, 1);
        };

        Assert.True(w.Quests.AddProgress(c.Character, Howl, HowlKill, 1));

        Assert.Equal(2, c.Character.Quests.Get(Howl)!.Stage);
        Assert.Equal(CharacterQuestState.Active, c.Character.Quests.Get(Howl)!.State);
        Assert.Equal(1u, c.Character.Quests.Get(Howl)!.ProgressOf(HowlTalk));
    }
}
