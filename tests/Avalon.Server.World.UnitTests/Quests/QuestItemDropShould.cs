using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Loot;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.Server.World.UnitTests.Parties;
using Avalon.World;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Loot;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Maps;
using Avalon.World.Quests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Quests.QuestTestData;

namespace Avalon.Server.World.UnitTests.Quests;

/// <summary>
/// A quest item drops only for a character that needs it (#433): one roll per eligible member with an unmet
/// Collect objective in its current stage, each success its own drop, reserved to that member for good.
/// </summary>
public class QuestItemDropShould
{
    private static uint s_nextCreature = 970_000;

    private static Creature Boar(MapInstance instance, QuestTestWorld w)
    {
        CreatureTemplate metadata = w.Data.CreatureTemplates.Single(t => t.Id.Value == QuestTestData.Boar);
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, Interlocked.Increment(ref s_nextCreature)),
            Metadata = metadata,
            TemplateId = metadata.Id,
            Position = Vector3.zero,
            Level = 1,
            Experience = 1,
        };
        instance.AddCreature(creature);
        return creature;
    }

    private static (MapInstance Instance, PartyClient A, PartyClient B) PartyOfTwo(QuestTestWorld w)
    {
        var p = new PartyTestWorld();
        PartyClient a = p.Online(1, "A", level: 1);
        PartyClient b = p.Online(2, "B", level: 1);
        p.Form(a, b);
        MapInstance instance = TestMapInstances.Build(w.World, ownerPartyId: p.Parties.PartyOf(a.Id)!.Id, parties: p.Parties, quests: w.Quests);
        MapInstanceClients.Join(instance, a.Character);
        MapInstanceClients.Join(instance, b.Character);
        return (instance, a, b);
    }

    private static List<GroundLoot> Tusks(MapInstance instance) =>
        instance.Drops.All.Where(d => d.ItemTemplateId?.Value == Tusk).OrderBy(d => d.OwnerCharacterId).ToList();

    [Fact]
    public async Task Drop_only_for_the_member_with_the_objective_and_reserve_it_for_good()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        (MapInstance instance, PartyClient a, PartyClient _) = PartyOfTwo(w);
        using (instance)
        {
            a.Character.Quests.Start(QuestTestData.Tusks, DateTime.UnixEpoch);

            instance.ReportKill(Boar(instance, w), a.Character);

            GroundLoot tusk = Assert.Single(Tusks(instance));
            Assert.Equal((a.Id, 1u, DateTime.MaxValue), (tusk.OwnerCharacterId, tusk.Count, tusk.FreeForAllAt));
        }
    }

    [Fact]
    public async Task Drop_one_for_each_member_who_needs_it()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        (MapInstance instance, PartyClient a, PartyClient b) = PartyOfTwo(w);
        using (instance)
        {
            a.Character.Quests.Start(QuestTestData.Tusks, DateTime.UnixEpoch);
            b.Character.Quests.Start(QuestTestData.Tusks, DateTime.UnixEpoch);

            instance.ReportKill(Boar(instance, w), b.Character);

            Assert.Equal([a.Id, b.Id], Tusks(instance).Select(d => d.OwnerCharacterId!.Value));
        }
    }

    [Fact]
    public async Task Roll_nothing_for_a_member_who_already_has_the_count()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        (MapInstance instance, PartyClient a, PartyClient _) = PartyOfTwo(w);
        using (instance)
        {
            a.Character.Quests.Start(QuestTestData.Tusks, DateTime.UnixEpoch);
            Assert.Equal(Avalon.World.Inventory.InventoryAddResult.Ok, w.Economy.InventoryOf(a.Character).TryAdd(new ItemTemplateId(Tusk), 2));

            instance.ReportKill(Boar(instance, w), a.Character);

            Assert.Empty(Tusks(instance));
        }
    }

    /// <summary>
    /// Review fix: the roll asks the Bag, as the pickup does, not the count recorded at the last flush. A member who
    /// picks up the tusk that completes the count and kills again in the same tick, before the flush recounts, gets
    /// no new reserved tusk it could never take.
    /// </summary>
    [Fact]
    public async Task Roll_nothing_for_a_member_whose_pickup_this_tick_completed_the_count()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        (MapInstance instance, PartyClient a, PartyClient _) = PartyOfTwo(w);
        using (instance)
        {
            ActiveQuest quest = a.Character.Quests.Start(QuestTestData.Tusks, DateTime.UnixEpoch);
            w.Economy.InventoryOf(a.Character).TryAdd(new ItemTemplateId(Tusk), 1);
            a.Character.Quests.SetProgress(quest, TusksCollect, 1);   // as the last flush recounted it
            instance.ReportKill(Boar(instance, w), a.Character);
            GroundLoot tusk = Assert.Single(Tusks(instance));
            Assert.Equal(LootPickupResult.Ok, LootPickup.TryPickUp(a.Character, instance.Drops, tusk.Guid, 100f, DateTime.UnixEpoch,
                w.Economy, NullLogger.Instance, w.Quests).Result);
            Assert.Equal(1u, quest.ProgressOf(TusksCollect));   // no flush yet

            instance.ReportKill(Boar(instance, w), a.Character);

            Assert.Empty(Tusks(instance));
        }
    }

    [Fact]
    public async Task Drop_nothing_when_the_roll_fails()
    {
        List<QuestTemplate> quests = [Quest(QuestTestData.Tusks).WithStage(0, Collect(TusksCollect, Tusk, 2, (QuestTestData.Boar, 60f)))];
        QuestTestWorld w = await QuestTestWorld.CreateAsync(quests);
        w.Random.Value = 0.6;   // 60.0 is not below 60
        (MapInstance instance, PartyClient a, PartyClient _) = PartyOfTwo(w);
        using (instance)
        {
            a.Character.Quests.Start(QuestTestData.Tusks, DateTime.UnixEpoch);

            instance.ReportKill(Boar(instance, w), a.Character);

            Assert.Empty(Tusks(instance));
        }
    }

    [Fact]
    public async Task Let_only_its_owner_pick_it_up_however_long_it_lies_there()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        (MapInstance instance, PartyClient a, PartyClient b) = PartyOfTwo(w);
        using (instance)
        {
            a.Character.Quests.Start(QuestTestData.Tusks, DateTime.UnixEpoch);
            instance.ReportKill(Boar(instance, w), a.Character);
            GroundLoot tusk = Assert.Single(Tusks(instance));
            DateTime late = DateTime.MaxValue.AddDays(-1);

            LootPickupOutcome other = LootPickup.TryPickUp(b.Character, instance.Drops, tusk.Guid, 100f, late, w.Economy, NullLogger.Instance);
            LootPickupOutcome owner = LootPickup.TryPickUp(a.Character, instance.Drops, tusk.Guid, 100f, late, w.Economy, NullLogger.Instance);

            Assert.Equal(LootPickupResult.NotYours, other.Result);
            Assert.Equal(LootPickupResult.Ok, owner.Result);
        }
    }

    /// <summary>Review fix: the instance has no loot table to roll, and the quest item still drops.</summary>
    [Fact]
    public async Task Drop_a_quest_item_with_no_loot_roller_configured()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        using MapInstance instance = TestMapInstances.Build(w.World, quests: w.Quests);   // no roller, no allocator
        MapInstanceClient a = MapInstanceClients.Join(instance, 1);
        a.Character.Quests.Start(QuestTestData.Tusks, DateTime.UnixEpoch);

        instance.ReportKill(Boar(instance, w), a.Character);

        Assert.Equal(a.Character.Guid.Id, Assert.Single(Tusks(instance)).OwnerCharacterId);
    }

    /// <summary>
    /// Ruling: a quest drop roll that throws costs the kill only its quest drops; the table loot still drops and the
    /// experience is still awarded. Kill credit reads the quests first and succeeds; only the roll after it throws.
    /// </summary>
    [Fact]
    public async Task Still_drop_the_table_loot_and_award_the_experience_when_the_quest_drop_roll_throws()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        int reads = 0;
        IWorld flaky = Substitute.For<IWorld>();
        flaky.Data.Returns(_ => reads++ == 0 ? w.Data : throw new InvalidOperationException("quest data unavailable"));
        var quests = new QuestService(flaky, Substitute.For<IServiceProvider>(), w.Economy, w.Random, w.Clock, NullLogger<QuestService>.Instance);
        ILootRoller roller = Substitute.For<ILootRoller>();
        roller.Roll(default!, default!, default!).ReturnsForAnyArgs([new RolledDrop(null, 0, 5)]);
        var allocator = new PartyLootAllocator(Options.Create(new GameConfiguration()), new FixedTimeProvider(DateTimeOffset.UnixEpoch),
            CombatRandom.Steady);
        using MapInstance instance = TestMapInstances.Build(w.World, navigator: Substitute.For<IMapNavigator>(),
            quests: quests, lootRoller: roller, lootAllocator: allocator);
        MapInstanceClient a = MapInstanceClients.Join(instance, 1);
        a.Character.Quests.Start(QuestTestData.Tusks, DateTime.UnixEpoch);   // no Kill objective: credit reads the data once
        ulong before = a.Character.Experience;

        instance.ReportKill(Boar(instance, w), a.Character);

        Assert.Equal(2, reads);   // the credit, then the roll that threw
        GroundLoot pile = Assert.Single(instance.Drops.All);
        Assert.Equal(5ul, pile.Gold);
        Assert.Equal(before + 1, a.Character.Experience);
    }

    /// <summary>Review fix: a drop rolled for a quest since abandoned is refused to its owner and stays on the ground.</summary>
    [Fact]
    public async Task Refuse_a_quest_item_its_owner_no_longer_needs_and_leave_it_lying()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        (MapInstance instance, PartyClient a, PartyClient _) = PartyOfTwo(w);
        using (instance)
        {
            a.Character.Quests.Start(QuestTestData.Tusks, DateTime.UnixEpoch);
            instance.ReportKill(Boar(instance, w), a.Character);
            GroundLoot tusk = Assert.Single(Tusks(instance));
            Assert.Equal(Avalon.Network.Packets.Quest.QuestResult.Ok, w.Quests.Abandon(a.Character, QuestTestData.Tusks));

            LootPickupOutcome outcome = LootPickup.TryPickUp(a.Character, instance.Drops, tusk.Guid, 100f, DateTime.UnixEpoch,
                w.Economy, NullLogger.Instance, w.Quests);

            Assert.Equal((LootPickupResult.NotYours, false), (outcome.Result, outcome.Removed));
            Assert.True(instance.Drops.TryGet(tusk.Guid, out _));
            Assert.Empty(a.Character.Container(InventoryType.Bag).Items);
        }
    }

    /// <summary>Review fix: a character whose bag already holds the count needs no more, so it takes none.</summary>
    [Fact]
    public async Task Refuse_a_quest_item_when_the_bag_already_holds_the_count()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        (MapInstance instance, PartyClient a, PartyClient _) = PartyOfTwo(w);
        using (instance)
        {
            a.Character.Quests.Start(QuestTestData.Tusks, DateTime.UnixEpoch);
            instance.ReportKill(Boar(instance, w), a.Character);
            GroundLoot tusk = Assert.Single(Tusks(instance));
            w.Economy.InventoryOf(a.Character).TryAdd(new ItemTemplateId(Tusk), 2);

            LootPickupOutcome outcome = LootPickup.TryPickUp(a.Character, instance.Drops, tusk.Guid, 100f, DateTime.UnixEpoch,
                w.Economy, NullLogger.Instance, w.Quests);

            Assert.Equal(LootPickupResult.NotYours, outcome.Result);
            Assert.True(instance.Drops.TryGet(tusk.Guid, out _));
        }
    }

    [Fact]
    public async Task Let_its_owner_take_a_quest_item_it_still_needs()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        (MapInstance instance, PartyClient a, PartyClient _) = PartyOfTwo(w);
        using (instance)
        {
            a.Character.Quests.Start(QuestTestData.Tusks, DateTime.UnixEpoch);
            instance.ReportKill(Boar(instance, w), a.Character);
            GroundLoot tusk = Assert.Single(Tusks(instance));

            LootPickupOutcome outcome = LootPickup.TryPickUp(a.Character, instance.Drops, tusk.Guid, 100f, DateTime.UnixEpoch,
                w.Economy, NullLogger.Instance, w.Quests);

            Assert.Equal((LootPickupResult.Ok, true), (outcome.Result, outcome.Removed));
            Assert.Equal(1L, QuestService.HeldInBag(a.Character, new ItemTemplateId(Tusk)));
        }
    }
}
