using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Loot;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.Server.World.UnitTests.Parties;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Loot;
using Avalon.World.Quests;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Avalon.Server.World.UnitTests.Quests.QuestTestData;

namespace Avalon.Server.World.UnitTests.Quests;

/// <summary>
/// A quest item drops only for a character that needs it (#433): one roll per eligible member with an unmet
/// Collect objective in its current stage, each success its own drop, reserved to that member for good.
/// </summary>
public class QuestItemDropShould
{
    private static uint _nextCreature = 970_000;

    private static Creature Boar(MapInstance instance, QuestTestWorld w)
    {
        CreatureTemplate metadata = w.Data.CreatureTemplates.Single(t => t.Id.Value == QuestTestData.Boar);
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, Interlocked.Increment(ref _nextCreature)),
            Metadata = metadata, TemplateId = metadata.Id, Position = Vector3.zero, Level = 1, Experience = 1,
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
            var quest = a.Character.Quests.Start(QuestTestData.Tusks, DateTime.UnixEpoch);
            a.Character.Quests.SetProgress(quest, TusksCollect, 2);

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
}
