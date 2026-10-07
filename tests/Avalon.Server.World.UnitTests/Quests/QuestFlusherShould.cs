using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Quest;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public.Characters;
using Avalon.World.Quests;
using static Avalon.Server.World.UnitTests.Quests.QuestTestData;

namespace Avalon.Server.World.UnitTests.Quests;

/// <summary>
/// QuestFlusher (#433): the whole log once, when the character first flushes; then at most one update per quest
/// per tick, display data only on an accept; the milestone lines on the system channel.
/// </summary>
public class QuestFlusherShould
{
    private static void Tick(QuestTestWorld w, QuestClient c) => QuestFlusher.Flush(c.Connection, w.Quests);

    private static List<SQuestUpdatePacket> Updates(QuestClient c) => c.Read<SQuestUpdatePacket>(NetworkPacketType.SMSG_QUEST_UPDATE);

    [Fact]
    public async Task Send_the_whole_log_once_on_entering_the_world()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        QuestClient c = w.Join();
        QuestTestWorld.Complete(c, Hunt);
        ActiveQuest quest = c.Character.Quests.Start(Tusks, DateTime.UnixEpoch);
        c.Character.Quests.SetProgress(quest, TusksCollect, 1);

        Tick(w, c);
        Tick(w, c);

        SQuestLogPacket log = Assert.Single(c.Read<SQuestLogPacket>(NetworkPacketType.SMSG_QUEST_LOG));
        QuestLogEntryDto entry = Assert.Single(log.Quests);
        Assert.Equal((Tusks, QuestStateKind.Active, 0, "A Test Quest"), (entry.QuestId, entry.State, entry.Stage, entry.Display!.Title));
        Assert.Equal(1u, Assert.Single(entry.Progress, p => p.ObjectiveId == TusksCollect).Progress);
        Assert.Equal([Hunt], log.CompletedQuestIds);
        Assert.Empty(Updates(c));   // the log already carried what the start marked
    }

    /// <summary>Review Focus 2.</summary>
    [Fact]
    public async Task Leave_a_quest_the_catalog_no_longer_has_out_of_the_log()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        QuestClient c = w.Join();
        c.Character.Quests.Start(99999, DateTime.UnixEpoch);
        c.Character.Quests.Start(Hunt, DateTime.UnixEpoch);

        Tick(w, c);

        Assert.Equal([Hunt], Assert.Single(c.Read<SQuestLogPacket>(NetworkPacketType.SMSG_QUEST_LOG)).Quests.Select(q => q.QuestId));
    }

    /// <summary>
    /// The select-time recount (#433) brings the loaded log up to the bag. What it changed is in the log the client
    /// gets on entering, so it is neither said again as fresh progress nor sent again as updates; it is still saved.
    /// </summary>
    [Fact]
    public async Task Not_replay_the_select_recount_as_fresh_progress_on_entering_the_world()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        QuestClient c = w.Join();
        c.Character.Quests.Load(new Avalon.Database.Character.Repositories.CharacterQuestRows(
            [new CharacterQuest { CharacterId = c.Id, QuestId = Tusks, State = CharacterQuestState.Active, Stage = 0, AcceptedAt = DateTime.UnixEpoch }],
            [],
            [new CharacterCompletedQuest { CharacterId = c.Id, QuestId = Hunt, CompletedAt = DateTime.UnixEpoch }]));
        Assert.Equal(InventoryAddResult.Ok, w.Economy.InventoryOf(c.Character).TryAdd(new ItemTemplateId(Tusk), 2));
        InventoryUpdateFlusher.Flush(c.Connection);   // the bag came with the load, not with this tick
        c.Clear();

        w.Quests.RecountAtSelect(c.Character);
        Tick(w, c);
        Tick(w, c);

        QuestLogEntryDto entry = Assert.Single(Assert.Single(c.Read<SQuestLogPacket>(NetworkPacketType.SMSG_QUEST_LOG)).Quests);
        Assert.Equal(QuestStateKind.ReadyToTurnIn, entry.State);
        Assert.Equal(2u, Assert.Single(entry.Progress, p => p.ObjectiveId == TusksCollect).Progress);
        Assert.Empty(c.Lines());
        Assert.Empty(Updates(c));
        Assert.True(c.Character.SaveState.HasChanges);
    }

    /// <summary>
    /// A quest whose log entry cannot be built (here a reward the display build trips over) is left out; the log still
    /// goes out once with the others, the lines go out once, nothing is left owed, and the markers are still sent.
    /// </summary>
    [Fact]
    public async Task Leave_a_quest_whose_entry_throws_out_of_the_log_and_send_the_rest()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        Creature giver = w.Place(Giver);
        QuestClient c = w.Join();
        w.Accept(c, Hunt);
        c.Character.Quests.Start(Tusks, DateTime.UnixEpoch);
        Assert.True(w.Data.Quests.TryGet(Tusks, out QuestView? tusks));
        ((IList<QuestItemRewardView>)tusks!.ItemRewards)[0] = null!;

        Tick(w, c);
        Tick(w, c);

        Assert.Equal([Hunt], Assert.Single(c.Read<SQuestLogPacket>(NetworkPacketType.SMSG_QUEST_LOG)).Quests.Select(q => q.QuestId));
        Assert.Equal(["Quest accepted: A Test Quest."], c.Lines());
        Assert.Empty(c.Character.Quests.ClientChanges);
        Assert.Empty(Updates(c));
        QuestMarkerDto marker = Assert.Single(Assert.Single(c.Read<SQuestMarkersPacket>(NetworkPacketType.SMSG_QUEST_MARKERS)).Markers);
        Assert.Equal(giver.Guid.RawValue, marker.CreatureGuid);
    }

    [Fact]
    public async Task Send_one_update_per_quest_per_tick_with_the_display_only_on_accept()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        QuestClient c = w.Join();
        Tick(w, c);
        c.Clear();

        w.Accept(c, Hunt);
        w.Quests.AddProgress(c.Character, Hunt, HuntKill, 1);
        Tick(w, c);

        SQuestUpdatePacket accepted = Assert.Single(Updates(c));
        Assert.Equal((QuestUpdateKind.Accepted, QuestStateKind.Active), (accepted.Kind, accepted.State));
        Assert.NotNull(accepted.Display);
        Assert.Equal(1u, Assert.Single(accepted.Progress, p => p.ObjectiveId == HuntKill).Progress);
        c.Clear();

        w.Quests.AddProgress(c.Character, Hunt, HuntKill, 1);
        Tick(w, c);

        SQuestUpdatePacket progress = Assert.Single(Updates(c));
        Assert.Equal((QuestUpdateKind.Progress, QuestStateKind.ReadyToTurnIn), (progress.Kind, progress.State));
        Assert.Null(progress.Display);
    }

    [Fact]
    public async Task Send_Removed_for_an_abandon_and_Completed_for_a_turn_in()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        QuestClient c = w.Join();
        w.Accept(c, Hunt);
        Tick(w, c);
        c.Clear();

        w.Quests.Abandon(c.Character, Hunt);
        Tick(w, c);
        Assert.Equal(QuestUpdateKind.Removed, Assert.Single(Updates(c)).Kind);
        c.Clear();

        w.Accept(c, Hunt);
        w.Quests.AddProgress(c.Character, Hunt, HuntKill, 2);
        Tick(w, c);
        c.Clear();
        Creature giver = w.Place(Giver);
        w.Talk(c, giver);
        Assert.Equal(QuestResult.Ok, w.Quests.TurnIn(c.Connection, c.Character, Hunt, giver.Guid.RawValue));
        Tick(w, c);
        Assert.Equal(QuestUpdateKind.Completed, Assert.Single(Updates(c)).Kind);
    }

    [Fact]
    public async Task Send_the_milestone_lines_on_the_system_channel_and_only_once()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        QuestClient c = w.Join();
        w.Accept(c, Hunt);
        w.Quests.AddProgress(c.Character, Hunt, HuntKill, 1);

        Tick(w, c);
        Tick(w, c);

        Assert.Equal(["Quest accepted: A Test Quest.", "Things done: 1/2"], c.Lines());
    }

    /// <summary>
    /// Final review M2: each flush step is contained on its own. A collect recount that throws on every tick that
    /// touches the bag (here a bag item with no template, which the recount trips on) is logged at Error, and the log
    /// and the markers still go out on that very tick.
    /// </summary>
    [Fact]
    public async Task Send_the_log_and_markers_when_the_recount_throws()
    {
        var log = new TestLog();
        QuestTestWorld w = await QuestTestWorld.CreateAsync(log: log);
        QuestClient c = w.Join();
        w.Place(Giver);
        c.Character.Quests.Start(Tusks, DateTime.UnixEpoch);
        w.Economy.InventoryOf(c.Character).TryAdd(new ItemTemplateId(Tusk), 1);   // marks a Bag slot
        InventoryItem tusk = Assert.Single(c.Character.Container(Avalon.World.Public.Enums.InventoryType.Bag).Items);
        c.Character.Container(Avalon.World.Public.Enums.InventoryType.Bag).Load(
        [
            tusk,
            new InventoryItem(1, new ItemInstanceId(Guid.CreateVersion7()), null!, 1, 0, ItemInstanceFlags.None, 0),
        ]);

        Tick(w, c);

        Assert.Single(c.Read<SQuestLogPacket>(NetworkPacketType.SMSG_QUEST_LOG));
        Assert.Single(c.Read<SQuestMarkersPacket>(NetworkPacketType.SMSG_QUEST_MARKERS));
        Assert.Contains(log.Errors, e => e.Message.Contains(QuestFlusher.RecountStep, StringComparison.Ordinal));
    }
}
