using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Quest;
using Avalon.World.Quests;
using Avalon.World.Reload;
using Xunit;
using static Avalon.Server.World.UnitTests.Quests.QuestTestData;

namespace Avalon.Server.World.UnitTests.Quests;

/// <summary>
/// #738: a /reload quests that lowers an objective's count or removes an objective settles every held quest once, on
/// the next flush, rather than leaving a quest that is now complete Active until its next kill, talk or bag change.
/// </summary>
public class QuestReloadSettleShould
{
    private const uint Pair = 7210, PairBoar = 72101, PairWolf = 72102;

    private static void Tick(QuestTestWorld w, QuestClient c) => QuestFlusher.Flush(c.Connection, w.Quests);

    private static List<SQuestUpdatePacket> Updates(QuestClient c) => c.Read<SQuestUpdatePacket>(NetworkPacketType.SMSG_QUEST_UPDATE);

    private static List<QuestTemplate> Quests() =>
    [
        Quest(Hunt).WithStage(0, Kill(HuntKill, Boar, 3)),
        Quest(Pair).WithStage(0, Kill(PairBoar, Boar, 1), Kill(PairWolf, Wolf, 1)).WithStage(1, Kill(HowlKill, Wolf, 2)),
    ];

    /// <summary>A world with both quests accepted and flushed once, so only what the reload causes is left to see.</summary>
    private static async Task<(QuestTestWorld W, QuestClient C, List<QuestTemplate> Rows)> WorldAsync()
    {
        List<QuestTemplate> rows = Quests();
        QuestTestWorld w = await QuestTestWorld.CreateAsync(rows);
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
