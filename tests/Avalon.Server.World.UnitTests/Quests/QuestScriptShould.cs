using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Quest;
using Avalon.World.Inventory;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Scripts;
using Avalon.World.Quests;
using Avalon.World.Scripts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using static Avalon.Server.World.UnitTests.Quests.QuestTestData;

namespace Avalon.Server.World.UnitTests.Quests;

/// <summary>
/// A quest's script (#433): its hooks run while the quest is held, it can narrow acceptance, its one write is
/// advancing its own Scripted objectives in the current stage, and a hook that throws never stops the quest.
/// </summary>
public class QuestScriptShould
{
    private const uint Scripted = 7601, ScriptedStep = 76011, ScriptedKill = 76012, ScriptedWolf = 76013;
    private const uint Other = 7602, OtherKill = 76021;
    private const uint Gather = 7603, GatherTusk = 76031, GatherStep = 76032;

    private static List<QuestTemplate> Quests() =>
    [
        Quest(Scripted, script: nameof(SampleQuestScript))
            .WithStage(0, QuestTestData.Scripted(ScriptedStep, 2), Kill(ScriptedKill, Boar, 1))
            .WithStage(1, Kill(ScriptedWolf, Wolf, 1)),
        Quest(Other, script: nameof(SampleQuestScript)).WithStage(0, Kill(OtherKill, Boar, 3)),
        Quest(Gather, script: nameof(SampleQuestScript))
            .WithStage(0, Collect(GatherTusk, Tusk, 1))
            .WithStage(1, QuestTestData.Scripted(GatherStep, 1)),
    ];

    private static async Task<(QuestTestWorld W, QuestScriptRecorder R, QuestClient C)> WorldAsync(TestLog? log = null)
    {
        var recorder = new QuestScriptRecorder();
        IServiceProvider services = new ServiceCollection().AddSingleton(recorder).BuildServiceProvider();
        var scripts = Substitute.For<IScriptManager>();
        scripts.GetQuestScript(nameof(SampleQuestScript)).Returns(typeof(SampleQuestScript));
        QuestTestWorld w = await QuestTestWorld.CreateAsync(Quests(), scripts: scripts, services: services, log: log);
        return (w, recorder, w.Join());
    }

    [Fact]
    public async Task Call_the_hooks_while_the_quest_is_held()
    {
        (QuestTestWorld w, QuestScriptRecorder r, QuestClient c) = await WorldAsync();

        w.Accept(c, Scripted);
        w.Quests.CreatureKilled(w.Place(Wolf), [c.Character]);
        w.Quests.Interacted(c.Character, w.Place(TalkTarget));

        Assert.Equal(["OnAccepted", "OnStageStarted:0", "OnCreatureKilled", "OnInteract"], r.Calls.Where(n => n != "CanAccept"));
        Assert.Equal((c.Id, "Tester1", (ushort)1, CharacterClass.Warrior),
            (r.LastCharacter!.CharacterId, r.LastCharacter.Name, r.LastCharacter.Level, r.LastCharacter.Class));
        Assert.Equal((new CreatureTemplateId(TalkTarget), "Marta", false), (r.LastCreature!.TemplateId, r.LastCreature.Name, r.LastCreature.IsDead));
    }

    [Fact]
    public async Task Call_no_hook_for_a_quest_not_held()
    {
        (QuestTestWorld w, QuestScriptRecorder r, QuestClient c) = await WorldAsync();

        w.Quests.CreatureKilled(w.Place(Boar), [c.Character]);
        w.Quests.Interacted(c.Character, w.Place(TalkTarget));
        QuestFlusher.Flush(c.Connection, w.Quests);

        Assert.Empty(r.Seen);
    }

    [Fact]
    public async Task Hide_the_quest_when_the_script_refuses_it()
    {
        (QuestTestWorld w, QuestScriptRecorder r, QuestClient c) = await WorldAsync();
        r.AllowAccept = false;

        Assert.True(w.Data.Quests.TryGet(Scripted, out QuestView? quest));
        Assert.Equal(QuestResult.NotAvailable, w.Quests.Availability(c.Character, quest!));
    }

    /// <summary>CanAccept only narrows: the data's own rules come first, so a script that allows cannot open a quest the data refuses.</summary>
    [Fact]
    public async Task Not_widen_what_the_data_refuses()
    {
        (QuestTestWorld w, QuestScriptRecorder r, QuestClient c) = await WorldAsync();
        w.Accept(c, Scripted);
        r.Calls.Clear();

        Assert.True(w.Data.Quests.TryGet(Scripted, out QuestView? quest));
        Assert.Equal(QuestResult.NotAvailable, w.Quests.Availability(c.Character, quest!));
        Assert.DoesNotContain("CanAccept", r.Calls);
    }

    [Fact]
    public async Task Advance_its_own_scripted_objective_capped_and_settle_the_stage()
    {
        (QuestTestWorld w, QuestScriptRecorder r, QuestClient c) = await WorldAsync();
        w.Accept(c, Scripted);
        r.AdvanceOnKill = (ScriptedStep, 5);

        w.Quests.CreatureKilled(w.Place(Boar), [c.Character]);   // the kill objective and the scripted one together

        Assert.Contains(true, r.AdvanceResults);
        Assert.Equal(1, c.Character.Quests.Get(Scripted)!.Stage);
        Assert.Equal(2u, c.Character.Quests.Get(Scripted)!.ProgressOf(ScriptedStep));
        Assert.Contains("OnStageStarted:1", r.Calls);
    }

    [Fact]
    public async Task Refuse_to_advance_anything_but_its_own_scripted_objective_in_the_current_stage()
    {
        (QuestTestWorld w, QuestScriptRecorder r, QuestClient c) = await WorldAsync();
        w.Accept(c, Scripted);
        IQuestContext context = ContextOf(r);

        Assert.False(context.Advance(ScriptedKill, 1));   // a Kill objective
        Assert.False(context.Advance(ScriptedWolf, 1));   // the next stage's
        Assert.False(context.Advance(OtherKill, 1));      // another quest's
        Assert.False(context.Advance(9999, 1));           // nobody's
        Assert.False(context.Advance(ScriptedStep, 0));   // nothing
        Assert.Equal(0u, context.ProgressOf(ScriptedKill));
        Assert.True(context.Advance(ScriptedStep, 1));
        Assert.Equal(1u, context.ProgressOf(ScriptedStep));
        Assert.Equal((Scripted, 0, c.Id), (context.QuestId, context.Stage, context.Character.CharacterId));
    }

    [Fact]
    public async Task Refuse_a_stale_context_once_the_quest_is_abandoned()
    {
        (QuestTestWorld w, QuestScriptRecorder r, QuestClient c) = await WorldAsync();
        w.Accept(c, Scripted);
        IQuestContext context = ContextOf(r);
        w.Quests.Abandon(c.Character, Scripted);
        w.Accept(c, Scripted);

        Assert.False(context.Advance(ScriptedStep, 1));
        Assert.Equal(0u, c.Character.Quests.Get(Scripted)!.ProgressOf(ScriptedStep));
    }

    [Fact]
    public async Task Refuse_to_advance_once_the_quest_is_ready()
    {
        (QuestTestWorld w, QuestScriptRecorder r, QuestClient c) = await WorldAsync();
        w.Accept(c, Gather);
        IQuestContext context = ContextOf(r);
        Assert.False(context.Advance(GatherStep, 1));    // stage 0 holds only the Collect objective
        w.Economy.InventoryOf(c.Character).TryAdd(new ItemTemplateId(Tusk), 1);
        w.Quests.RecountCollect(c.Character);
        context = ContextOf(r);
        Assert.True(context.Advance(GatherStep, 1));

        Assert.Equal(CharacterQuestState.ReadyToTurnIn, c.Character.Quests.Get(Gather)!.State);
        Assert.False(context.Advance(GatherStep, 1));
    }

    [Fact]
    public async Task Keep_the_quest_going_when_a_hook_throws_and_log_it_once_per_ten_seconds()
    {
        var log = new TestLog();
        (QuestTestWorld w, QuestScriptRecorder r, QuestClient c) = await WorldAsync(log);
        r.ThrowIn = "OnCreatureKilled";
        w.Accept(c, Scripted);

        w.Quests.CreatureKilled(w.Place(Boar), [c.Character]);
        w.Quests.CreatureKilled(w.Place(Boar), [c.Character]);

        Assert.Equal(1u, c.Character.Quests.Get(Scripted)!.ProgressOf(ScriptedKill));
        (LogLevel _, Exception? exception, string message) = Assert.Single(log.Errors);
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains($"Quest {Scripted}", message, StringComparison.Ordinal);
        Assert.Contains("OnCreatureKilled", message, StringComparison.Ordinal);
        Assert.Contains($"character {c.Id}", message, StringComparison.Ordinal);

        w.Clock.Advance(TimeSpan.FromSeconds(10));
        w.Quests.CreatureKilled(w.Place(Wolf), [c.Character]);
        Assert.Equal(2, log.Errors.Count());
        Assert.Contains("1 earlier throws", log.Errors.Last().Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_every_other_quests_hook_when_one_quests_hook_throws()
    {
        (QuestTestWorld w, QuestScriptRecorder r, QuestClient c) = await WorldAsync();
        w.Accept(c, Scripted);
        w.Accept(c, Other);
        r.ThrowIn = "OnCreatureKilled";
        r.ThrowOnlyFor = Scripted;
        r.Seen.Clear();

        w.Quests.CreatureKilled(w.Place(Boar), [c.Character]);

        Assert.Equal([(Scripted, "OnCreatureKilled"), (Other, "OnCreatureKilled")], r.Seen);
        Assert.Equal(1u, c.Character.Quests.Get(Other)!.ProgressOf(OtherKill));
    }

    [Fact]
    public async Task Accept_the_quest_when_its_accept_hook_throws()
    {
        (QuestTestWorld w, QuestScriptRecorder r, QuestClient c) = await WorldAsync();
        r.ThrowIn = "OnAccepted";

        w.Accept(c, Scripted);   // asserts Ok

        Assert.True(c.Character.Quests.IsActive(Scripted));
        Assert.Contains("OnStageStarted:0", r.Calls);
    }

    [Fact]
    public async Task Credit_the_talk_when_the_interact_hook_throws()
    {
        List<QuestTemplate> quests =
        [
            Quest(Scripted, script: nameof(SampleQuestScript)).WithStage(0, Talk(ScriptedStep, TalkTarget)).WithStage(1, Kill(ScriptedWolf, Wolf, 1)),
        ];
        var recorder = new QuestScriptRecorder { ThrowIn = "OnInteract" };
        var scripts = Substitute.For<IScriptManager>();
        scripts.GetQuestScript(nameof(SampleQuestScript)).Returns(typeof(SampleQuestScript));
        QuestTestWorld w = await QuestTestWorld.CreateAsync(quests, scripts: scripts,
            services: new ServiceCollection().AddSingleton(recorder).BuildServiceProvider());
        QuestClient c = w.Join();
        w.Accept(c, Scripted);

        w.Quests.Interacted(c.Character, w.Place(TalkTarget));

        Assert.Equal(1, c.Character.Quests.Get(Scripted)!.Stage);
    }

    [Fact]
    public async Task Refuse_the_quest_when_its_can_accept_throws()
    {
        (QuestTestWorld w, QuestScriptRecorder r, QuestClient c) = await WorldAsync();
        r.ThrowIn = "CanAccept";

        Assert.True(w.Data.Quests.TryGet(Scripted, out QuestView? quest));
        Assert.Equal(QuestResult.NotAvailable, w.Quests.Availability(c.Character, quest!));
    }

    [Fact]
    public async Task Refuse_a_quest_whose_script_cannot_be_built()
    {
        var log = new TestLog();
        var scripts = Substitute.For<IScriptManager>();
        scripts.GetQuestScript("Unbuildable").Returns(typeof(UnbuildableQuestScript));
        QuestTestWorld w = await QuestTestWorld.CreateAsync(
            [Quest(Scripted, script: "Unbuildable").WithStage(0, Kill(ScriptedKill, Boar, 1))],
            scripts: scripts, services: new ServiceCollection().BuildServiceProvider(), log: log);
        QuestClient c = w.Join();

        Assert.True(w.Data.Quests.TryGet(Scripted, out QuestView? quest));
        Assert.Equal(QuestResult.NotAvailable, w.Quests.Availability(c.Character, quest!));
        Assert.Equal(QuestResult.NotAvailable, w.Quests.Availability(c.Character, quest!));
        Assert.Single(log.Errors);   // built once, and the failure remembered
    }

    [Fact]
    public async Task Run_the_enter_instance_hook_once_per_instance()
    {
        (QuestTestWorld w, QuestScriptRecorder r, QuestClient c) = await WorldAsync();
        w.Accept(c, Scripted);

        QuestFlusher.Flush(c.Connection, w.Quests);
        QuestFlusher.Flush(c.Connection, w.Quests);

        Assert.Single(r.Calls, n => n == "OnEnterInstance");
        Assert.Equal(w.Instance.InstanceId, r.LastInstance!.InstanceId);
    }

    /// <summary>
    /// The select-time recount runs before the character stands in any instance: a stage it settles still starts its
    /// script (that hook needs no instance, and can advance), and OnEnterInstance waits until the character is in one.
    /// </summary>
    [Fact]
    public async Task Start_a_stage_at_select_before_any_instance_and_enter_the_instance_later()
    {
        (QuestTestWorld w, QuestScriptRecorder r, QuestClient c) = await WorldAsync();
        c.Character.Quests.Start(Gather, DateTime.UnixEpoch);   // loaded from the log, as at select
        c.Character.InstanceId = Guid.NewGuid();                // no live instance has this id
        Assert.Equal(InventoryAddResult.Ok, w.Economy.InventoryOf(c.Character).TryAdd(new ItemTemplateId(Tusk), 1));
        r.OnStage = (context, stage) =>
        {
            if (stage == 1)
                r.AdvanceResults.Add(context.Advance(GatherStep, 1));
        };

        w.Quests.RecountCollect(c.Character);
        QuestFlusher.Flush(c.Connection, w.Quests);

        Assert.Equal(["OnStageStarted:1"], r.Calls);
        Assert.Equal([true], r.AdvanceResults);
        Assert.Equal(CharacterQuestState.ReadyToTurnIn, c.Character.Quests.Get(Gather)!.State);

        c.Character.InstanceId = w.Instance.InstanceId;
        QuestFlusher.Flush(c.Connection, w.Quests);
        Assert.Equal(["OnStageStarted:1", "OnEnterInstance"], r.Calls);
    }

    private static IQuestContext ContextOf(QuestScriptRecorder r) =>
        r.LastContext ?? throw new InvalidOperationException("no hook ran yet");

    /// <summary>Its constructor wants something no container has; it lives in the test assembly, which the constructibility test leaves out.</summary>
    public sealed class UnbuildableQuestScript(UnbuildableQuestScript.Missing missing) : QuestScript
    {
        public sealed class Missing;

        public Missing Dependency { get; } = missing;
    }
}
