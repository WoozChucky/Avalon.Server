using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Quest;
using Avalon.Server.World.UnitTests.Handlers;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.Server.World.UnitTests.Parties;
using Avalon.World.ChunkLayouts;
using Avalon.World.Doodads;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Maps.Navigation;
using Avalon.World.Quests;
using Avalon.World;
using Avalon.World.Inventory;
using Avalon.World.Testing;
using Avalon.World.Testing.Scenarios;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Quests.QuestTestData;

namespace Avalon.Server.World.UnitTests.Quests;

/// <summary>
/// A doodad use credits the use-doodad objectives of its user alone (ruling R12), through a real instance and the use
/// service: +1, capped, on each UseDoodad objective for the template in the current stage of an Active quest, settled as a
/// kill is. A party member standing beside the user on the same step gets nothing, and a use that credits nothing
/// allocates nothing. Quest 7801 and doodad guids 7801 and 7802 are this class's.
/// </summary>
public class QuestDoodadCreditShould
{
    private const uint Errand = 7801, ErrandUse = 78011, ErrandTalk = 78012;

    // Pull the lever, then talk to Marta; and a kill quest that no doodad moves.
    private static List<QuestTemplate> Quests() =>
    [
        Quest(Hunt).WithStage(0, Kill(HuntKill, Boar, 2)),
        Quest(Errand).WithStage(0, UseDoodad(ErrandUse, Lever, 1)).WithStage(1, Talk(ErrandTalk, TalkTarget)),
    ];

    [Fact]
    public async Task Credit_the_user_on_the_step_and_nobody_else()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync(Quests(), doodads: QuestDoodads());
        Assert.True(w.Data.Doodads.TryGet(new DoodadTemplateId(Lever), out DoodadTemplateView? lever));
        var scripts = new DoodadScripts(new ServiceCollection().BuildServiceProvider(), w.Clock, NullLogger<DoodadScripts>.Instance);
        var visibility = new DoodadVisibility(scripts);
        var uses = new DoodadUseService(scripts, visibility, w.World, w.Clock, NullLogger<DoodadUseService>.Instance, w.Quests);
        using MapInstance instance = TestMapInstances.Build(w.World, quests: w.Quests, doodadVisibility: visibility, doodadUses: uses);
        var doodad = new Doodad(new ObjectGuid(ObjectType.Doodad, Errand), lever!, new Vector3(2f, 0f, 0f), 0f);
        instance.AddDoodad(doodad);

        // A and B in one party, both on the lever step, side by side.
        var p = new PartyTestWorld();
        PartyClient partyA = p.Online(1, "A");
        PartyClient partyB = p.Online(2, "B");
        p.Form(partyA, partyB);
        MapInstanceClient a = MapInstanceClients.Join(instance, partyA.Character);
        MapInstanceClient b = MapInstanceClients.Join(instance, partyB.Character);
        a.Character.Position = Vector3.zero;
        b.Character.Position = new Vector3(1f, 0f, 0f);
        foreach (CharacterEntity character in (CharacterEntity[])[a.Character, b.Character])
        {
            character.Quests.Start(Errand, DateTime.UnixEpoch);
            character.Quests.LogSent = true;
            character.Quests.ClearClientChanges();
        }

        uses.Interact(a.Connection, a.Character, instance, doodad.Guid);

        ActiveQuest user = a.Character.Quests.Get(Errand)!;
        Assert.Equal(1u, user.ProgressOf(ErrandUse));
        Assert.Equal(1, user.Stage);
        ActiveQuest member = b.Character.Quests.Get(Errand)!;
        Assert.Empty(member.Progress);
        Assert.Equal(0, member.Stage);

        w.Quests.FlushClient(a.Connection, a.Character);
        SQuestUpdatePacket update = Assert.Single(a.Read<SQuestUpdatePacket>(NetworkPacketType.SMSG_QUEST_UPDATE));
        Assert.Equal((Errand, QuestUpdateKind.Progress, QuestStateKind.Active, 1), (update.QuestId, update.Kind, update.State, update.Stage));
        w.Quests.FlushClient(b.Connection, b.Character);
        Assert.Empty(b.Read<SQuestUpdatePacket>(NetworkPacketType.SMSG_QUEST_UPDATE));

        // A user without the quest, holding one the lever does not move: unchanged, and the walk allocates nothing.
        QuestService quiet = w.QuietQuests();
        CharacterEntity stranger = TestCharacters.New(3);
        stranger.Quests.Start(Hunt, DateTime.UnixEpoch);
        quiet.DoodadUsed(stranger, lever!.Id);
        long before = ThreadAllocations.BeforeQuietWindow();
        quiet.DoodadUsed(stranger, lever.Id);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.Empty(stranger.Quests.Get(Hunt)!.Progress);
        Assert.Equal(CharacterQuestState.Active, stranger.Quests.Get(Hunt)!.State);
    }

    /// <summary>
    /// The seeded strongbox end to end (quest 9's objective 901, doodad 5, as the World database seeds them): shown to two
    /// players on the step, opened by one through its cast bar, it credits that user alone, whose next view decision
    /// removes it. It stays ready and in the other player's view, who then opens it for their own credit and remove.
    /// </summary>
    [Fact]
    public void Hide_the_seeded_strongbox_from_its_user_once_opened()
    {
        const uint LostCaravan = 9, Opened = 901;
        StaticData data = ScenarioReferenceData.Shared.Data;
        Assert.True(data.Doodads.TryGet(new DoodadTemplateId(5), out DoodadTemplateView? strongbox));
        var clock = new ManualTimerClock();
        IWorld world = MapInstanceClients.NewWorld(data);
        var quests = new QuestService(world, Substitute.For<IServiceProvider>(),
            new CharacterEconomy(world, new ItemIdAllocator()), new SteadyLootRandom(0), clock, NullLogger<QuestService>.Instance);
        var scripts = new DoodadScripts(new ServiceCollection().BuildServiceProvider(), clock, NullLogger<DoodadScripts>.Instance);
        var visibility = new DoodadVisibility(scripts);
        var uses = new DoodadUseService(scripts, visibility, world, clock, NullLogger<DoodadUseService>.Instance, quests);
        using MapInstance instance = TestMapInstances.Build(world, time: clock, quests: quests, doodadVisibility: visibility,
            doodadUses: uses);
        var doodad = new Doodad(new ObjectGuid(ObjectType.Doodad, 7802), strongbox!, new Vector3(2f, 0f, 0f), 0f);
        instance.AddDoodad(doodad);
        MapInstanceClient user = MapInstanceClients.Join(instance, 1);
        MapInstanceClient beside = MapInstanceClients.Join(instance, 2);
        user.Character.Position = Vector3.zero;
        beside.Character.Position = new Vector3(1f, 0f, 0f);
        user.Character.Quests.Start(LostCaravan, DateTime.UnixEpoch);
        beside.Character.Quests.Start(LostCaravan, DateTime.UnixEpoch);
        Tick(instance, 30);
        Assert.Contains(user.Added(), s => s.Guid == doodad.Guid.RawValue);
        Assert.Contains(beside.Added(), s => s.Guid == doodad.Guid.RawValue);

        uses.Interact(user.Connection, user.Character, instance, doodad.Guid);
        clock.Advance(TimeSpan.FromMilliseconds(1500));
        Tick(instance, 6);

        ActiveQuest opened = user.Character.Quests.Get(LostCaravan)!;
        Assert.Equal((1u, 1), (opened.ProgressOf(Opened), opened.Stage));
        Assert.Contains(doodad.Guid.RawValue, user.Removed());
        // Never spent: still ready, still in the view of the player beside, whose quest the use did not move.
        Assert.Equal(DoodadState.Ready, doodad.State);
        Assert.Equal(0, beside.Character.Quests.Get(LostCaravan)!.Stage);
        Assert.DoesNotContain(doodad.Guid.RawValue, beside.Removed());

        // So that player opens it too, and gets their own credit and their own remove.
        uses.Interact(beside.Connection, beside.Character, instance, doodad.Guid);
        clock.Advance(TimeSpan.FromMilliseconds(1500));
        Tick(instance, 6);

        ActiveQuest theirs = beside.Character.Quests.Get(LostCaravan)!;
        Assert.Equal((1u, 1), (theirs.ProgressOf(Opened), theirs.Stage));
        Assert.Contains(doodad.Guid.RawValue, beside.Removed());
    }

    /// <summary>
    /// The seeded notice board end to end (quest 9's objective 902, doodad 6, MapDoodadSpawns row 1): a Glimmerdell
    /// instance places it, both players see it, the one still on step 0 cannot use it, and the reader on step 1 can, for
    /// the credit that moves their quest on.
    /// </summary>
    [Fact]
    public async Task Credit_the_seeded_notice_board_only_to_a_player_on_its_step()
    {
        const uint LostCaravan = 9, Read = 902;
        ChunkLayout town = ScenarioReferenceData.Shared.TownLayout;
        var clock = new ManualTimerClock();
        IWorld world = MapInstanceClients.NewWorld(ScenarioReferenceData.Shared.Data);
        var quests = new QuestService(world, Substitute.For<IServiceProvider>(),
            new CharacterEconomy(world, new ItemIdAllocator()), new SteadyLootRandom(0), clock, NullLogger<QuestService>.Instance);
        var scripts = new DoodadScripts(new ServiceCollection().BuildServiceProvider(), clock, NullLogger<DoodadScripts>.Instance);
        var visibility = new DoodadVisibility(scripts);
        var uses = new DoodadUseService(scripts, visibility, world, clock, NullLogger<DoodadUseService>.Instance, quests);
        var navigator = new MapNavigator(NullLoggerFactory.Instance);
        navigator.LoadFromNavMesh(TownNavmesh.Shared);
        using MapInstance instance = TestMapInstances.Build(world, navigator: navigator, layout: town,
            templateId: ScenarioReferenceData.TownMap, time: clock, quests: quests, doodadVisibility: visibility, doodadUses: uses);
        using var database = SqliteDatabase.World();
        await new DoodadPlacementService(scripts, clock, NullLoggerFactory.Instance, new MapDoodadSpawnRepository(database))
            .PlaceAsync(instance, town, DoodadPlan.Empty, ScenarioReferenceData.TownMap, CancellationToken.None);
        Doodad board = Assert.Single(instance.Doodads.All);

        MapInstanceClient passer = MapInstanceClients.Join(instance, 1);
        MapInstanceClient reader = MapInstanceClients.Join(instance, 2);
        passer.Character.Position = board.Position + new Vector3(1.5f, 0f, 0f);
        reader.Character.Position = board.Position + new Vector3(1.5f, 0f, 1f);
        passer.Character.Quests.Start(LostCaravan, DateTime.UnixEpoch);
        ActiveQuest reading = reader.Character.Quests.Start(LostCaravan, DateTime.UnixEpoch);
        reader.Character.Quests.SetStage(reading, 1);
        Tick(instance, 30);
        // Both see it; only the reader is told it can use it.
        Assert.Null(Assert.Single(passer.Added(), s => s.Guid == board.Guid.RawValue).CanInteract);
        Assert.True(Assert.Single(reader.Added(), s => s.Guid == board.Guid.RawValue).CanInteract);

        uses.Interact(passer.Connection, passer.Character, instance, board.Guid);
        uses.Interact(reader.Connection, reader.Character, instance, board.Guid);
        Tick(instance, 6);

        ActiveQuest passing = passer.Character.Quests.Get(LostCaravan)!;
        Assert.Equal((0u, 0), (passing.ProgressOf(Read), passing.Stage));
        Assert.Equal(2, reader.Character.Quests.Get(LostCaravan)!.Stage);
    }

    private static void Tick(MapInstance instance, int count)
    {
        for (int i = 0; i < count; i++)
            instance.Update(TimeSpan.FromSeconds(1d / 60d));
    }

    /// <summary>A credit that throws costs the use its credit only: the once-per-instance doodad is still spent, so it cannot be used again.</summary>
    [Fact]
    public async Task Still_spend_the_doodad_when_the_quest_credit_throws()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync(Quests(), doodads: QuestDoodads());
        Assert.True(w.Data.Doodads.TryGet(new DoodadTemplateId(Lever), out DoodadTemplateView? lever));
        var log = new TestLog();
        var scripts = new DoodadScripts(new ServiceCollection().BuildServiceProvider(), w.Clock, NullLogger<DoodadScripts>.Instance);
        var visibility = new DoodadVisibility(scripts);
        var uses = new DoodadUseService(scripts, visibility, w.World, w.Clock, new Logger<DoodadUseService>(log), w.ThrowingQuests());
        using MapInstance instance = TestMapInstances.Build(w.World, quests: w.Quests, doodadVisibility: visibility, doodadUses: uses);
        var doodad = new Doodad(new ObjectGuid(ObjectType.Doodad, Errand), lever!, new Vector3(2f, 0f, 0f), 0f);
        instance.AddDoodad(doodad);
        MapInstanceClient a = MapInstanceClients.Join(instance, 1);
        a.Character.Position = Vector3.zero;
        a.Character.Quests.Start(Errand, DateTime.UnixEpoch);

        uses.Interact(a.Connection, a.Character, instance, doodad.Guid);

        Assert.Equal(DoodadState.Used, doodad.State);
        Assert.Empty(a.Character.Quests.Get(Errand)!.Progress);
        Assert.Single(log.Errors);
    }
}
