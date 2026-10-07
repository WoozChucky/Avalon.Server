using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Quest;
using Avalon.Network.Packets.World;
using Avalon.World.Inventory;
using Avalon.World.Quests;
using Avalon.World.Reload;
using Microsoft.Extensions.Logging;
using static Avalon.Server.World.UnitTests.Quests.QuestTestData;

namespace Avalon.Server.World.UnitTests.Quests;

/// <summary>
/// #738: a /reload quests that lowers an objective's count or removes an objective settles every held quest once, on
/// the next flush, rather than leaving a quest that is now complete Active until its next kill, talk or bag change.
/// </summary>
public class QuestReloadSettleShould
{
    private const uint Pair = 7210, PairBoar = 72101, PairWolf = 72102;
    private const uint Gather = 7211, GatherTusk = 72111;

    private static void Tick(QuestTestWorld w, QuestClient c) => QuestFlusher.Flush(c.Connection, w.Quests);

    private static List<SQuestUpdatePacket> Updates(QuestClient c) => c.Read<SQuestUpdatePacket>(NetworkPacketType.SMSG_QUEST_UPDATE);

    private static List<QuestTemplate> Quests() =>
    [
        Quest(Hunt).WithStage(0, Kill(HuntKill, Boar, 3)),
        Quest(Pair).WithStage(0, Kill(PairBoar, Boar, 1), Kill(PairWolf, Wolf, 1)).WithStage(1, Kill(HowlKill, Wolf, 2)),
        Quest(Gather).WithStage(0, Collect(GatherTusk, Tusk, 2)),
    ];

    /// <summary>A world with both quests accepted and flushed once, so only what the reload causes is left to see.</summary>
    private static async Task<(QuestTestWorld W, QuestClient C, List<QuestTemplate> Rows)> WorldAsync(TestLog? log = null)
    {
        List<QuestTemplate> rows = Quests();
        QuestTestWorld w = await QuestTestWorld.CreateAsync(rows, log: log);
        QuestClient c = w.Join();
        w.Accept(c, Hunt);
        w.Accept(c, Pair);
        Assert.True(w.Quests.AddProgress(c.Character, Hunt, HuntKill, 2));
        Assert.True(w.Quests.AddProgress(c.Character, Pair, PairBoar, 1));
        Tick(w, c);
        c.Clear();
        return (w, c, rows);
    }

    private static async Task ReloadAsync(QuestTestWorld w) => w.Data.Apply(await w.Data.PrepareAsync(ReloadArea.Quests));

    [Fact]
    public async Task Make_a_quest_ready_once_a_reload_lowered_its_last_count_to_what_it_holds()
    {
        (QuestTestWorld w, QuestClient c, List<QuestTemplate> rows) = await WorldAsync();
        rows.Single(q => q.Id.Value == Hunt).Objectives.Single().Count = 2;
        await ReloadAsync(w);

        Tick(w, c);

        Assert.Equal(CharacterQuestState.ReadyToTurnIn, c.Character.Quests.Get(Hunt)!.State);
        Assert.Contains("A Test Quest: ready to turn in.", c.Lines());
        SQuestUpdatePacket update = Assert.Single(Updates(c));
        Assert.Equal((Hunt, QuestStateKind.ReadyToTurnIn), (update.QuestId, update.State));
    }

    [Fact]
    public async Task Cap_a_count_a_reload_lowered_below_what_was_held()
    {
        (QuestTestWorld w, QuestClient c, List<QuestTemplate> rows) = await WorldAsync();
        rows.Single(q => q.Id.Value == Hunt).Objectives.Single().Count = 1;
        await ReloadAsync(w);

        Tick(w, c);

        ActiveQuest hunt = c.Character.Quests.Get(Hunt)!;
        Assert.Equal((CharacterQuestState.ReadyToTurnIn, 1u), (hunt.State, hunt.ProgressOf(HuntKill)));
    }

    [Fact]
    public async Task Start_the_next_stage_once_a_reload_removed_the_objective_it_waited_on()
    {
        (QuestTestWorld w, QuestClient c, List<QuestTemplate> rows) = await WorldAsync();
        QuestTemplate pair = rows.Single(q => q.Id.Value == Pair);
        pair.Objectives.Remove(pair.Objectives.Single(o => o.Id == PairWolf));
        await ReloadAsync(w);

        Tick(w, c);

        ActiveQuest active = c.Character.Quests.Get(Pair)!;
        Assert.Equal((CharacterQuestState.Active, 1), (active.State, active.Stage));
        Assert.Contains("A Test Quest: stage complete.", c.Lines());
    }

    [Fact]
    public async Task Settle_once_per_reload()
    {
        (QuestTestWorld w, QuestClient c, List<QuestTemplate> rows) = await WorldAsync();
        rows.Single(q => q.Id.Value == Hunt).Objectives.Single().Count = 2;
        await ReloadAsync(w);
        Tick(w, c);
        c.Clear();

        Tick(w, c);

        Assert.Empty(Updates(c));
        Assert.Empty(c.Lines());
    }

    [Fact]
    public async Task Change_nothing_without_a_reload()
    {
        (QuestTestWorld w, QuestClient c, _) = await WorldAsync();

        Tick(w, c);

        Assert.Equal(CharacterQuestState.Active, c.Character.Quests.Get(Hunt)!.State);
        Assert.Empty(Updates(c));
        Assert.Empty(c.Lines());
    }

    /// <summary>Hunt made ready by its kills and flushed, so only what the reload causes is left to see.</summary>
    private static async Task<(QuestTestWorld W, QuestClient C, List<QuestTemplate> Rows)> ReadyHuntAsync()
    {
        (QuestTestWorld w, QuestClient c, List<QuestTemplate> rows) = await WorldAsync();
        Assert.True(w.Quests.AddProgress(c.Character, Hunt, HuntKill, 1));
        Assert.Equal(CharacterQuestState.ReadyToTurnIn, c.Character.Quests.Get(Hunt)!.State);
        Tick(w, c);
        c.Clear();
        return (w, c, rows);
    }

    /// <summary>Fix round 1: a reload that raises a Collect count past what the Bag holds sends a ready quest back to Active.</summary>
    [Fact]
    public async Task Send_a_ready_quest_back_to_active_when_a_reload_raised_its_collect_count()
    {
        (QuestTestWorld w, QuestClient c, List<QuestTemplate> rows) = await WorldAsync();
        Assert.Equal(InventoryAddResult.Ok, w.Economy.InventoryOf(c.Character).TryAdd(new ItemTemplateId(Tusk), 2));
        w.Accept(c, Gather);
        Assert.Equal(CharacterQuestState.ReadyToTurnIn, c.Character.Quests.Get(Gather)!.State);
        Tick(w, c);
        c.Clear();

        rows.Single(q => q.Id.Value == Gather).Objectives.Single().Count = 3;
        await ReloadAsync(w);
        Tick(w, c);

        ActiveQuest gather = c.Character.Quests.Get(Gather)!;
        Assert.Equal((CharacterQuestState.Active, 2u), (gather.State, gather.ProgressOf(GatherTusk)));
        Assert.Contains("A Test Quest: no longer ready to turn in.", c.Lines());
        Assert.Equal(QuestStateKind.Active, Assert.Single(Updates(c)).State);
    }

    [Fact]
    public async Task Send_a_ready_quest_back_to_active_when_a_reload_added_a_collect_objective_to_its_last_stage()
    {
        (QuestTestWorld w, QuestClient c, List<QuestTemplate> rows) = await ReadyHuntAsync();
        rows.RemoveAll(q => q.Id.Value == Gather);   // the tusk may be collected by one quest only
        QuestObjective tusk = Collect(72012, Tusk, 1);
        tusk.QuestId = Hunt;
        tusk.StageSequence = 0;
        rows.Single(q => q.Id.Value == Hunt).Objectives.Add(tusk);
        await ReloadAsync(w);

        Tick(w, c);

        Assert.Equal(CharacterQuestState.Active, c.Character.Quests.Get(Hunt)!.State);
        Assert.Contains("A Test Quest: no longer ready to turn in.", c.Lines());
    }

    [Fact]
    public async Task Start_a_stage_a_reload_added_after_a_ready_quests_last()
    {
        (QuestTestWorld w, QuestClient c, List<QuestTemplate> rows) = await ReadyHuntAsync();
        rows.Single(q => q.Id.Value == Hunt).WithStage(1, Kill(72013, Wolf, 1));
        await ReloadAsync(w);

        Tick(w, c);

        ActiveQuest hunt = c.Character.Quests.Get(Hunt)!;
        Assert.Equal((CharacterQuestState.Active, 1), (hunt.State, hunt.Stage));
        Assert.Contains("A Test Quest: stage complete.", c.Lines());
    }

    /// <summary>Fix round 1: a quest whose current stage a reload removed cannot be settled; it is logged at Warning, throttled.</summary>
    [Fact]
    public async Task Warn_once_about_a_held_quest_whose_current_stage_a_reload_removed()
    {
        var log = new TestLog();
        (QuestTestWorld w, QuestClient c, List<QuestTemplate> rows) = await WorldAsync(log);
        Assert.True(w.Quests.AddProgress(c.Character, Pair, PairWolf, 1));
        Assert.Equal(1, c.Character.Quests.Get(Pair)!.Stage);
        QuestTemplate pair = rows.Single(q => q.Id.Value == Pair);
        pair.Objectives.RemoveAll(o => o.StageSequence == 1);
        pair.Stages.RemoveAll(stage => stage.Sequence == 1);
        await ReloadAsync(w);
        Tick(w, c);

        rows.Single(q => q.Id.Value == Hunt).Objectives.Single().Count = 4;
        await ReloadAsync(w);
        Tick(w, c);

        string warning = Assert.Single(log.Entries, e => e.Level == LogLevel.Warning).Message;
        Assert.Contains($"{Pair}", warning, StringComparison.Ordinal);
        Assert.Equal((CharacterQuestState.Active, 1), (c.Character.Quests.Get(Pair)!.State, c.Character.Quests.Get(Pair)!.Stage));
    }

    /// <summary>Fix round 1: a turn-in on the tick a reload landed, before any flush, sees the settled quest.</summary>
    [Fact]
    public async Task Let_a_turn_in_on_the_reload_tick_see_the_settled_quest()
    {
        (QuestTestWorld w, QuestClient c, List<QuestTemplate> rows) = await WorldAsync();
        rows.Single(q => q.Id.Value == Hunt).Objectives.Single().Count = 2;
        await ReloadAsync(w);
        Avalon.World.Public.Creatures.ICreature giver = w.Place(Giver);
        w.Talk(c, giver);

        Assert.Equal(QuestResult.Ok, w.Quests.TurnIn(c.Connection, c.Character, Hunt, giver.Guid.RawValue));
        Assert.True(c.Character.Quests.IsCompleted(Hunt));
    }

    /// <summary>Fix round 1: the NPC's options on the reload tick already offer the turn-in the reload made possible.</summary>
    [Fact]
    public async Task Offer_the_turn_in_on_the_reload_tick()
    {
        (QuestTestWorld w, QuestClient c, List<QuestTemplate> rows) = await WorldAsync();
        rows.Single(q => q.Id.Value == Hunt).Objectives.Single().Count = 2;
        await ReloadAsync(w);
        Avalon.World.Public.Creatures.ICreature giver = w.Place(Giver);

        List<SDialogueOptionInfo> options = w.Quests.DialogueOptionsFor(c.Connection, c.Character, giver, w.Data.Dialogue.GetRoot(giver.Metadata.Id)!);

        Assert.Contains(options, o => o.QuestId == Hunt && o.Kind == Avalon.Network.Packets.World.DialogueOptionKind.QuestTurnIn);
    }

    /// <summary>
    /// A reload that landed while the character was offline is settled by the select recount, quietly: the log the
    /// client gets on entering already shows it, and the flushes after it settle nothing more.
    /// </summary>
    [Fact]
    public async Task Settle_at_select_what_a_reload_made_complete_and_not_again_on_the_first_flush()
    {
        List<QuestTemplate> rows = Quests();
        rows.Single(q => q.Id.Value == Hunt).Objectives.Single().Count = 2;
        QuestTestWorld w = await QuestTestWorld.CreateAsync(rows);
        QuestClient c = w.Join();
        c.Character.Quests.Load(new Avalon.Database.Character.Repositories.CharacterQuestRows(
            [new CharacterQuest { CharacterId = c.Id, QuestId = Hunt, State = CharacterQuestState.Active, Stage = 0, AcceptedAt = DateTime.UnixEpoch }],
            [new CharacterQuestObjective { CharacterId = c.Id, QuestId = Hunt, ObjectiveId = HuntKill, Progress = 3 }],
            []));

        w.Quests.RecountAtSelect(c.Character);
        Tick(w, c);
        Tick(w, c);

        ActiveQuest hunt = c.Character.Quests.Get(Hunt)!;
        Assert.Equal((CharacterQuestState.ReadyToTurnIn, 2u), (hunt.State, hunt.ProgressOf(HuntKill)));
        Assert.Empty(Updates(c));
        Assert.Empty(c.Lines());
    }

    /// <summary>A reload after select is settled on the next flush, as for a character that was already playing.</summary>
    [Fact]
    public async Task Settle_a_reload_that_lands_after_select()
    {
        List<QuestTemplate> rows = Quests();
        QuestTestWorld w = await QuestTestWorld.CreateAsync(rows);
        QuestClient c = w.Join();
        c.Character.Quests.Load(new Avalon.Database.Character.Repositories.CharacterQuestRows(
            [new CharacterQuest { CharacterId = c.Id, QuestId = Hunt, State = CharacterQuestState.Active, Stage = 0, AcceptedAt = DateTime.UnixEpoch }],
            [new CharacterQuestObjective { CharacterId = c.Id, QuestId = Hunt, ObjectiveId = HuntKill, Progress = 2 }],
            []));
        w.Quests.RecountAtSelect(c.Character);
        Tick(w, c);
        c.Clear();

        rows.Single(q => q.Id.Value == Hunt).Objectives.Single().Count = 2;
        await ReloadAsync(w);
        Tick(w, c);

        Assert.Equal(CharacterQuestState.ReadyToTurnIn, c.Character.Quests.Get(Hunt)!.State);
        Assert.Single(Updates(c));
    }
}
