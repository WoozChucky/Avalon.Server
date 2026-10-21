using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Quest;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.Server.World.UnitTests.Quests;
using Avalon.World.Entities;
using Avalon.World.Loot;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Avalon.World.Quests;
using Avalon.World.Testing;
using static Avalon.Server.World.UnitTests.Quests.QuestTestData;

namespace Avalon.Server.World.UnitTests.Performance;

/// <summary>
/// What the quest service does for every kill, every tick a Bag changed and every pickup allocates nothing while no
/// quest moves, and a tick's quest updates only what they carry (#883). No scenario runs quests, so the scenario gate
/// would see none of it come back.
/// </summary>
/// <remarks>
/// The service runs over a world that is no substitute (<see cref="QuestTestWorld.QuietQuests" />): a substitute
/// allocates on every call it records. In the collection that runs alone, because the updates are sent.
/// </remarks>
[Collection(nameof(ScenarioAllocations))]
public class QuestAllocationShould
{
    private const int Rounds = 100;
    private const uint Wolves = 7701, WolvesKill = 77011, Gather = 7702, GatherTusk = 77021, Errand = 7703, ErrandTalk = 77031;
    private const uint Chore = 7704;

    // Held together: a kill quest for wolves, a collect quest whose item boars drop, and a conversation. And a daily
    // (#710) the tests above never hold, for the reset.
    private static List<QuestTemplate> Quests() =>
    [
        Quest(Wolves).WithStage(0, Kill(WolvesKill, Wolf, 1_000_000)),
        Quest(Gather).WithStage(0, Collect(GatherTusk, Tusk, 2, (Boar, 60f))),
        Quest(Errand).WithStage(0, Talk(ErrandTalk, TalkTarget)),
        Quest(Chore).WithStage(0, Kill(77041, Boar, 1)).Repeating(QuestRepeatFrequency.Daily),
    ];

    [Fact]
    public async Task Credit_a_kill_recount_a_bag_change_and_judge_a_pickup_allocating_nothing_when_no_quest_moves()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync(Quests());
        QuestService quests = w.QuietQuests();
        CharacterEntity character = HoldingAll(quests);
        // A boar is no objective of the three, and the tusk it may drop is still needed: the roll is made, and fails.
        w.Random.Value = 0.999;
        Creature boar = w.Place(Boar);
        var tusk = new ItemTemplateId(Tusk);
        List<ICharacter> eligible = [character];
        List<(RolledDrop Drop, uint Owner)> rolled = [];

        long killed = Quiet(() =>
        {
            quests.CreatureKilled(boar, eligible);
            quests.RollQuestDrops(boar, eligible, rolled);
        });
        long recounted = Quiet(() => quests.RecountCollect(character));
        long judged = Quiet(() => quests.MayPickUp(character, tusk));

        Assert.Multiple(
            () => Assert.Equal(0, killed),
            () => Assert.Equal(0, recounted),
            () => Assert.Equal(0, judged));

        // The same calls do move a quest when there is something to move, so the windows above walked the quests.
        w.Random.Value = 0;
        quests.RollQuestDrops(boar, eligible, rolled);
        quests.CreatureKilled(w.Place(Wolf), eligible);
        character.Container(InventoryType.Bag).Load([TestCharacters.Item(0, TuskItem(), count: 2)]);
        quests.RecountCollect(character);
        Assert.Equal((RolledDrop.Item(tusk, 1), character.Guid.Id), Assert.Single(rolled));
        Assert.Equal(1u, character.Quests.Get(Wolves)!.ProgressOf(WolvesKill));
        Assert.Equal(CharacterQuestState.ReadyToTurnIn, character.Quests.Get(Gather)!.State);
        Assert.False(quests.MayPickUp(character, tusk));
    }

    [Fact]
    public async Task Send_a_ticks_quest_updates_allocating_only_what_they_carry()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync(Quests());
        QuestService quests = w.QuietQuests();
        CharacterEntity character = HoldingAll(quests);
        character.Quests.LogSent = true;
        var connection = new QuietConnection(character);
        ActiveQuest wolves = character.Quests.Get(Wolves)!;
        ActiveQuest errand = character.Quests.Get(Errand)!;

        long flushed = ThreadAllocations.FewestOfThreeWindows(Rounds, () =>
        {
            // Two quests owe an update, the later one marked first.
            character.Quests.SetStage(errand, 0);
            character.Quests.SetStage(wolves, 0);
            quests.FlushClient(connection, character);
        });

        // An update is a message of its own with the count of each of the quest's objectives: built here to be measured.
        long carried = ThreadAllocations.FewestOfThreeWindows(Rounds, () =>
        {
            GC.KeepAlive(Update(objectives: 1));
            GC.KeepAlive(Update(objectives: 1));
        });

        Assert.Equal(carried, flushed);
        Assert.Equal(2 * 4 * Rounds, connection.Sent);
    }

    /// <summary>#710: the per-tick reset check and settling a reset for a character allocate nothing.</summary>
    [Fact]
    public async Task Look_for_a_reset_and_settle_one_allocating_nothing()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync(Quests());
        QuestService quests = w.QuietQuests();
        CharacterEntity character = TestCharacters.New();
        DateTime start = w.Clock.GetUtcNow().UtcDateTime, day = start;
        character.Quests.Complete(Chore, start);
        int gates = character.Quests.GateVersion;

        long idle = Quiet(() => quests.Resets.Advance(start));               // before the next boundary
        long crossed = Quiet(() => { day = day.AddDays(1); quests.Resets.Advance(day); quests.ApplyResets(character); });

        Assert.Multiple(() => Assert.Equal(0, idle), () => Assert.Equal(0, crossed));
        // Quiet runs its round Rounds times to warm up and Rounds times measured, each a day later: every crossing
        // reached the character.
        Assert.Equal(gates + 2 * Rounds, character.Quests.GateVersion);
    }

    /// <summary>A character holding the three quests, with one tusk of the two in its Bag, counted and with nothing owed.</summary>
    private static CharacterEntity HoldingAll(QuestService quests)
    {
        CharacterEntity character = TestCharacters.New();
        character.Container(InventoryType.Bag).Load([TestCharacters.Item(0, TuskItem())]);
        foreach (uint quest in (uint[])[Wolves, Gather, Errand])
            character.Quests.Start(quest, DateTime.UtcNow);

        quests.RecountCollect(character);
        character.Quests.ClearClientChanges();
        return character;
    }

    /// <summary>What <see cref="Rounds" /> calls allocate on this thread, after as many to warm up, in a window that reads nothing else.</summary>
    private static long Quiet(Action round)
    {
        for (int i = 0; i < Rounds; i++)
            round();

        long before = ThreadAllocations.BeforeQuietWindow();
        for (int i = 0; i < Rounds; i++)
            round();

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static SQuestUpdatePacket Update(int objectives)
    {
        var update = new SQuestUpdatePacket { QuestId = 1 };
        List<QuestProgressDto> progress = new(objectives);
        for (int i = 0; i < objectives; i++)
            progress.Add(new QuestProgressDto { ObjectiveId = 1 });

        update.Progress = progress;
        return update;
    }
}
