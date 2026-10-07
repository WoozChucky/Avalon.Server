using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.State;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.World.Abilities;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Items;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Creatures;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>MapInstance hosts item cast bars with its own cast ids and the existing cast packets.</summary>
public class MapInstanceItemUseShould
{
    private static readonly ItemTemplateId s_scroll = new(3);

    private static PendingItemUse Pending(MapInstanceClient client, List<string> ends, float seconds = 3f) => new()
    {
        Character = client.Character,
        Item = s_scroll,
        StartPosition = client.Character.Position,
        CastId = 0,
        CastTimeSeconds = seconds,
        CanComplete = () => true,
        Completed = () => ends.Add("completed"),
        Interrupted = () => ends.Add("interrupted"),
    };

    [Fact]
    public void Number_item_casts_from_the_instances_own_cast_ids()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());

        uint first = instance.ItemUses.TakeCastId();
        uint second = instance.ItemUses.TakeCastId();

        Assert.NotEqual(0u, first);
        Assert.Equal(first + 1, second);
    }

    [Fact]
    public void Send_the_start_and_finish_naming_the_item_and_complete_on_its_tick()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        MapInstanceClient client = Join(instance, 7);
        var ends = new List<string>();
        uint castId = instance.ItemUses.TakeCastId();
        instance.ItemUses.Start(new PendingItemUse
        {
            Character = client.Character,
            Item = s_scroll,
            StartPosition = client.Character.Position,
            CastId = castId,
            CastTimeSeconds = 3f,
            CanComplete = () => true,
            Completed = () => ends.Add("completed"),
            Interrupted = () => ends.Add("interrupted"),
        });

        instance.Update(TimeSpan.FromSeconds(3.1));

        SUnitStartCastPacket start = Assert.Single(client.Read<SUnitStartCastPacket>(NetworkPacketType.SMSG_UNIT_START_CAST));
        Assert.Equal((3ul, 0u, castId, 3f), (start.ItemTemplateId, start.AbilityId, start.CastId, start.CastTime));
        SUnitFinishCastPacket finish = Assert.Single(client.Read<SUnitFinishCastPacket>(NetworkPacketType.SMSG_UNIT_FINISH_CAST));
        Assert.Equal((3ul, castId), (finish.ItemTemplateId, finish.CastId));
        Assert.Equal(["completed"], ends);
    }

    /// <summary>Leaving (a transfer, a logout) ends the bar for every watcher and answers the use.</summary>
    [Fact]
    public void Interrupt_an_item_cast_when_its_caster_leaves()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        MapInstanceClient caster = Join(instance, 7);
        MapInstanceClient watcher = Join(instance, 8);
        var ends = new List<string>();
        instance.ItemUses.Start(Pending(caster, ends));

        instance.RemoveCharacter(caster.Connection);

        Assert.Equal(["interrupted"], ends);
        Assert.False(instance.ItemUses.IsCasting(caster.Character.Guid));
        SCharacterInterruptedCastPacket heard =
            Assert.Single(watcher.Read<SCharacterInterruptedCastPacket>(NetworkPacketType.SMSG_INTERRUPTED_CAST));
        Assert.Equal(3ul, heard.ItemTemplateId);
    }

    [Fact]
    public void Restore_health_through_its_own_combat_service()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        MapInstanceClient client = Join(instance, 7);
        client.Character.Health = 100;
        client.Character.CurrentHealth = 50;

        uint restored = ((IItemUseHost)instance).RestoreHealth(client.Character, client.Character, 30);

        Assert.Equal(30u, restored);
        Assert.Equal(80u, client.Character.CurrentHealth);
        Assert.Single(client.Read<SUnitHealedPacket>(NetworkPacketType.SMSG_UNIT_HEALED));
    }

    private static (MapInstance Instance, MapInstanceClient Client, GameAbility Ability) CasterWithCostlyCircle(uint castTimeMs = 0)
    {
        MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler _);
        MapInstanceClient client = Join(instance, 7);
        AbilityTemplate row = AbilityTestData.Circle(1);
        row.Cost = 30;
        row.CostPowerType = PowerType.Mana;
        row.CastTime = castTimeMs;
        GameAbility ability = AbilityTestData.Game(row);
        client.Character.Spells.Load([ability]);
        client.Character.PowerType = PowerType.Mana;
        client.Character.Power = 100;
        client.Character.CurrentPower = 10;
        return (instance, client, ability);
    }

    [Fact]
    public void Cast_an_ability_for_an_item_without_its_cost_when_free()
    {
        (MapInstance instance, MapInstanceClient client, GameAbility ability) = CasterWithCostlyCircle();
        using (instance)
        {
            var aim = new AbilityAim(AbilityAim.FacingFromYaw(0f), null);

            Assert.False(((IItemUseHost)instance).CastForItem(client.Character, aim, ability, free: false));
            Assert.True(((IItemUseHost)instance).CastForItem(client.Character, aim, ability, free: true));

            Assert.Equal(10u, client.Character.CurrentPower);
            Assert.Single(client.Read<SUnitFinishCastPacket>(NetworkPacketType.SMSG_UNIT_FINISH_CAST));
        }
    }

    [Fact]
    public void Offer_the_instances_hit_query()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());

        Assert.Same(instance.Hits, ((IItemUseHost)instance).Hits);
    }

    private static Creature Summon(MapInstance instance, uint id)
    {
        var creature = new Creature
        {
            Guid = new Avalon.Common.ObjectGuid(Avalon.Common.ObjectType.Creature, id),
            Metadata = Loot.LootTestData.BoarTemplate(null),
            Position = new Avalon.Common.Mathematics.Vector3(0, 0, 3),
            Health = 100,
            CurrentHealth = 100,
            Level = 1,
        };
        instance.AddCreature(creature);
        return creature;
    }

    /// <summary>A summon leaves the instance once its lifetime has passed on the instance's clock.</summary>
    [Fact]
    public void Remove_a_summon_once_its_lifetime_has_passed_on_the_instance_clock()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        using MapInstance instance = TestMapInstances.Build(NewWorld(), time: clock);
        Join(instance, 7);   // an instance nobody is in does not tick
        Creature summon = Summon(instance, 90);
        ((IItemUseHost)instance).DespawnAfter(summon, TimeSpan.FromMinutes(5));

        clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
        instance.Update(TimeSpan.FromMilliseconds(16));
        Assert.True(instance.Creatures.ContainsKey(summon.Guid));

        clock.Advance(TimeSpan.FromSeconds(1));
        instance.Update(TimeSpan.FromMilliseconds(16));
        Assert.False(instance.Creatures.ContainsKey(summon.Guid));
    }

    /// <summary>A summon killed first is the corpse removal's, not the lifetime's.</summary>
    [Fact]
    public void Leave_a_killed_summon_to_the_corpse_removal()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        using MapInstance instance = TestMapInstances.Build(NewWorld(), time: clock);
        Join(instance, 7);
        Creature summon = Summon(instance, 91);
        ((IItemUseHost)instance).DespawnAfter(summon, TimeSpan.FromMinutes(5));

        summon.CurrentHealth = 0;
        clock.Advance(TimeSpan.FromMinutes(6));
        instance.Update(TimeSpan.FromMilliseconds(16));

        Assert.True(instance.Creatures.ContainsKey(summon.Guid));   // no corpse timer was registered in this test
    }

    /// <summary>A queued free cast for an item pays nothing and is still heard: its start is broadcast.</summary>
    [Fact]
    public void Queue_a_free_cast_for_an_item_without_its_cost_and_broadcast_its_start()
    {
        (MapInstance instance, MapInstanceClient client, GameAbility ability) = CasterWithCostlyCircle(castTimeMs: 1500);
        using (instance)
        {
            var aim = new AbilityAim(AbilityAim.FacingFromYaw(0f), null);

            Assert.True(((IItemUseHost)instance).CastForItem(client.Character, aim, ability, free: true));

            Assert.Equal(10u, client.Character.CurrentPower);
            SUnitStartCastPacket start = Assert.Single(client.Read<SUnitStartCastPacket>(NetworkPacketType.SMSG_UNIT_START_CAST));
            Assert.Equal(ability.AbilityId.Value, start.AbilityId);
        }
    }

    /// <summary>Item casts and ability casts number from one counter, so their ids never meet and only grow.</summary>
    [Fact]
    public void Share_the_cast_id_counter_between_item_and_ability_casts()
    {
        (MapInstance instance, MapInstanceClient client, GameAbility ability) = CasterWithCostlyCircle();
        using (instance)
        {
            var aim = new AbilityAim(AbilityAim.FacingFromYaw(0f), null);

            uint item = instance.ItemUses.TakeCastId();
            Assert.True(((IItemUseHost)instance).CastForItem(client.Character, aim, ability, free: true));
            uint cast = Assert.Single(client.Read<SUnitFinishCastPacket>(NetworkPacketType.SMSG_UNIT_FINISH_CAST)).CastId;
            uint next = instance.ItemUses.TakeCastId();

            Assert.True(item < cast && cast < next, $"item {item}, ability {cast}, item {next}");
        }
    }

    /// <summary>A summon that leaves takes its threat with it: it is no longer in the encounter it fought in.</summary>
    [Fact]
    public void Drop_an_expired_summon_from_its_encounter()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        using MapInstance instance = TestMapInstances.Build(NewWorld(), time: clock);
        MapInstanceClient client = Join(instance, 7);
        Creature summon = Summon(instance, 92);
        ((IItemUseHost)instance).DespawnAfter(summon, TimeSpan.FromMinutes(5));
        instance.CombatService.ApplyDamage(client.Character, summon, 1);
        Assert.NotNull(instance.CombatService.GetEncounterFor(summon));

        clock.Advance(TimeSpan.FromMinutes(5));
        instance.Update(TimeSpan.FromMilliseconds(16));

        Assert.False(instance.Creatures.ContainsKey(summon.Guid));
        Assert.Null(instance.CombatService.GetEncounterFor(summon));
    }

    /// <summary>A summon removed before its time is forgotten: a later tick neither throws nor removes it again.</summary>
    [Fact]
    public void Forget_a_summon_removed_before_its_time()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        using MapInstance instance = TestMapInstances.Build(NewWorld(), time: clock);
        Join(instance, 7);
        Creature summon = Summon(instance, 93);
        ((IItemUseHost)instance).DespawnAfter(summon, TimeSpan.FromMinutes(5));

        instance.RemoveCreature(summon);
        Assert.Null(Record.Exception(() => instance.Update(TimeSpan.FromMilliseconds(16))));

        // Back in the instance (as a script hot reload re-adds a creature), it is no longer a summon.
        instance.AddCreature(summon);
        clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Null(Record.Exception(() => instance.Update(TimeSpan.FromMilliseconds(16))));

        Assert.True(instance.Creatures.ContainsKey(summon.Guid));
    }

    /// <summary>One summon whose removal throws costs neither the tick nor the other summons their removal.</summary>
    [Fact]
    public void Remove_the_other_summons_when_one_throws()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        using MapInstance instance = TestMapInstances.Build(NewWorld(), time: clock);
        Join(instance, 7);

        // Once armed, its health reads 100 when the expired summons are gathered and throws when the removal reads it.
        bool armed = false;
        int reads = 0;
        ICreature broken = Substitute.For<Avalon.World.Public.Creatures.ICreature>();
        broken.Guid.Returns(new Avalon.Common.ObjectGuid(Avalon.Common.ObjectType.Creature, 94));
        broken.Name.Returns("Broken");
        ICreatureMetadata metadata = Substitute.For<Avalon.World.Public.Creatures.ICreatureMetadata>();
        metadata.Id.Returns(new CreatureTemplateId(4));
        broken.Metadata.Returns(metadata);
        broken.CurrentHealth.Returns(_ => armed && ++reads == 2 ? throw new InvalidOperationException("boom") : 100u);
        instance.AddCreature(broken);
        ((IItemUseHost)instance).DespawnAfter(broken, TimeSpan.FromMinutes(5));
        Creature summon = Summon(instance, 95);
        ((IItemUseHost)instance).DespawnAfter(summon, TimeSpan.FromMinutes(5));

        clock.Advance(TimeSpan.FromMinutes(5));
        armed = true;
        Exception? escaped = Record.Exception(() => instance.Update(TimeSpan.FromMilliseconds(16)));

        Assert.Null(escaped);
        Assert.True(reads >= 2, $"reads {reads}");
        Assert.False(instance.Creatures.ContainsKey(summon.Guid));
    }
}
