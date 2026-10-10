using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.State;
using Avalon.Network.Packets.World;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using NSubstitute;
using ProtoBuf;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>
/// Casting through a real MapInstance, the real cast handler, the shape scripts and CombatService.
/// Character ids (164_101 to 164_252) and creature ids (164_9xx) are unique to this class.
/// </summary>
public class MapInstanceAbilityCastShould
{
    private static readonly TimeSpan s_tick = TimeSpan.FromSeconds(1d / 60d);

    /// <summary>Seven 60 Hz ticks: past the 0.1 s state broadcast interval.</summary>
    private static void TickUntilBroadcast(MapInstance instance)
    {
        for (int i = 0; i < 7; i++)
        {
            instance.Update(s_tick);
        }
    }

    private static MapInstance BuildCasting(out CastAbilityHandler handler, MapType mapType = MapType.Normal) =>
        TestMapInstances.BuildCasting(out handler, mapType);

    /// <summary>A real creature at <paramref name="position" /> whose script takes each hit off its health.</summary>
    private static Creature AddCreature(MapInstance instance, uint id, Vector3 position, uint health, uint currentHealth = 0)
    {
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, id),
            Metadata = Loot.LootTestData.BoarTemplate(null),
            Position = position,
            Health = health,
            CurrentHealth = currentHealth == 0 ? health : currentHealth,
        };
        creature.Script = new WoundScript(creature);
        instance.AddCreature(creature);
        return creature;
    }

    /// <summary>A creature takes its damage through its script; this one only loses the health it is hit for.</summary>
    internal sealed class WoundScript(ICreature creature) : AiScript(creature, Substitute.For<ISimulationContext>())
    {
        public override object State { get; set; } = 0;

        protected override bool ShouldRun() => false;

        public override void OnHit(IUnit attacker, uint damage) =>
            Creature.CurrentHealth = damage >= Creature.CurrentHealth ? 0u : Creature.CurrentHealth - damage;
    }

    private static AbilityTemplate Timed(AbilityTemplate template, uint castTimeMs)
    {
        template.CastTime = castTimeMs;
        return template;
    }

    /// <summary>#521 item 9: other clients learn which ability a unit is casting, not just for how long.</summary>
    [Fact]
    public void Name_the_ability_on_the_start_cast_broadcast()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        MapInstanceClient caster = Join(instance, 164_101);
        MapInstanceClient watcher = Join(instance, 164_102);
        IAbility ability = Substitute.For<IAbility>();
        ability.AbilityId.Returns(new AbilityId(211));
        ability.Metadata.Returns(new AbilityMetadata { Name = "Flame Burst", ScriptName = "x", CastTime = 0.75f });
        ability.CastTimeTimer.Returns(0.6f);   // #627: the time the cast system set, haste included

        instance.BroadcastUnitStartCast(caster.Character, ability, 1u, null);

        foreach (MapInstanceClient client in new[] { caster, watcher })
        {
            SUnitStartCastPacket start = Assert.Single(client.Read<SUnitStartCastPacket>(NetworkPacketType.SMSG_UNIT_START_CAST));
            Assert.Equal(caster.Character.Guid.RawValue, start.Caster);
            Assert.Equal(211u, start.AbilityId);
            Assert.Equal(0.6f, start.CastTime);
        }
    }

    /// <summary>The handler builds the shape script by name and the instance tells every client where it fired (#164).</summary>
    [Fact]
    public void Fire_a_circle_through_the_handler_and_tell_everyone_where()
    {
        using MapInstance instance = BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient caster = Join(instance, 164_141);
        MapInstanceClient watcher = Join(instance, 164_142);
        watcher.Character.Health = 100;
        watcher.Character.CurrentHealth = 100;
        Assert.False(watcher.Character.IsDead);
        Assert.Equal(caster.Character.Position, watcher.Character.Position);   // inside the circle
        caster.Character.Spells.Load([AbilityTestData.Game(AbilityTestData.Circle(201, radius: 3f))]);

        handler.Execute(caster.Connection, new CCastAbilityPacket { AbilityId = 201 });

        Assert.Empty(caster.Read<SAbilityNotReadyPacket>(NetworkPacketType.SMSG_ABILITY_NOT_READY));
        SAbilityFiredPacket fired = Assert.Single(watcher.Read<SAbilityFiredPacket>(NetworkPacketType.SMSG_ABILITY_FIRED));
        Assert.Equal(caster.Character.Guid.RawValue, fired.CasterGuid);
        Assert.Equal(201u, fired.AbilityId);
        Assert.Null(fired.Direction);
        Assert.NotNull(fired.Centre);
        Assert.Single(caster.Read<SAbilityFiredPacket>(NetworkPacketType.SMSG_ABILITY_FIRED));

        // The watcher stands inside the circle, but an unflagged player is not hostile.
        Assert.Empty(watcher.Read<SCharacterDamagePacket>(NetworkPacketType.SMSG_CHARACTER_DAMAGED));
        Assert.Equal(watcher.Character.Health, watcher.Character.CurrentHealth);
    }

    /// <summary>
    /// A point-blank projectile ends on the tick after it spawns, between two state broadcasts. Clients
    /// still see it (#164): one add, then its final state (zero velocity) on the next broadcast, then
    /// one remove, in that order; and the creature is hit once.
    /// </summary>
    [Fact]
    public void Show_a_point_blank_projectile_spawn_its_final_state_and_its_despawn_in_order()
    {
        using MapInstance instance = BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient caster = Join(instance, 164_161);
        MapInstanceClient watcher = Join(instance, 164_162);
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, 164_961u),
            Metadata = Loot.LootTestData.BoarTemplate(null),
            Position = new Vector3(0f, 0f, 1f),   // one metre in front of the caster
            Health = 100,
            CurrentHealth = 100,
        };
        AiScript ai = Substitute.For<AiScript>(creature, Substitute.For<ISimulationContext>());
        creature.Script = ai;   // a creature takes its damage through its script
        instance.AddCreature(creature);
        caster.Character.Spells.Load([AbilityTestData.Game(AbilityTestData.Projectile(210, reach: 5f, speed: 20f))]);

        handler.Execute(caster.Connection,
            new CCastAbilityPacket { AbilityId = 210, GroundPos = new Vector3Dto { X = 0f, Y = 0f, Z = 5f } });
        for (int i = 0; i < 30; i++)
        {
            instance.Update(s_tick);
        }

        List<(string Kind, ObjectState? State)> seen = [];
        foreach (OutboundPacket packet in watcher.Sent)
        {
            if (packet.Header.Type == NetworkPacketType.SMSG_WORLD_STATE_ADD)
            {
                seen.AddRange(Decode<SInstanceStateAddPacket>(packet).Adds
                    .Where(s => new ObjectGuid(s.Guid).Type == ObjectType.SpellProjectile)
                    .Select(s => ("add", (ObjectState?)s)));
            }
            else if (packet.Header.Type == NetworkPacketType.SMSG_WORLD_STATE_UPDATE)
            {
                seen.AddRange(Decode<SInstanceStateUpdatePacket>(packet).Updates
                    .Where(s => new ObjectGuid(s.Guid).Type == ObjectType.SpellProjectile)
                    .Select(s => ("update", (ObjectState?)s)));
            }
            else if (packet.Header.Type == NetworkPacketType.SMSG_WORLD_STATE_REMOVE)
            {
                seen.AddRange(Decode<SInstanceStateRemovePacket>(packet).Removes
                    .Where(g => new ObjectGuid(g).Type == ObjectType.SpellProjectile)
                    .Select(_ => ("remove", (ObjectState?)null)));
            }
        }

        Assert.Equal(["add", "update", "remove"], seen.Select(s => s.Kind));
        Assert.Equal(seen[0].State!.Guid, seen[1].State!.Guid);
        Assert.Equal(0f, seen[1].State!.Velocity!.X);
        Assert.Equal(0f, seen[1].State!.Velocity!.Z);
        ai.Received(1).OnHit(caster.Character, 10u);   // hit once
    }

    /// <summary>
    /// End to end (#164): two flagged players hurt each other outside a town, and never in one; the row outside it
    /// keeps the town row from passing vacuously.
    /// </summary>
    [Theory]
    [InlineData(MapType.Normal, 90u, 1)]
    [InlineData(MapType.Town, 100u, 0)]
    public void Let_two_flagged_players_hurt_each_other_only_outside_a_town(MapType mapType, uint healthLeft, int damagePackets)
    {
        using MapInstance instance = BuildCasting(out CastAbilityHandler handler, mapType);
        (MapInstanceClient a, MapInstanceClient b) = FlaggedPair(instance, 164_201, 164_202);

        handler.Execute(a.Connection, new CCastAbilityPacket { AbilityId = 201 });

        Assert.Empty(a.Read<SAbilityNotReadyPacket>(NetworkPacketType.SMSG_ABILITY_NOT_READY));
        Assert.Equal(healthLeft, b.Character.CurrentHealth);
        Assert.Equal(damagePackets, b.Read<SCharacterDamagePacket>(NetworkPacketType.SMSG_CHARACTER_DAMAGED).Count);
    }

    /// <summary>Two flagged players a metre apart; the first holds a 3 m circle around itself (201).</summary>
    private static (MapInstanceClient A, MapInstanceClient B) FlaggedPair(MapInstance instance, uint first, uint second)
    {
        MapInstanceClient a = Join(instance, first);
        MapInstanceClient b = Join(instance, second);
        a.Character.Data!.PvpEnabled = true;
        b.Character.Data!.PvpEnabled = true;
        b.Character.Health = 100;
        b.Character.CurrentHealth = 100;
        b.Character.Position = new Vector3(1f, 0f, 0f);
        a.Character.Spells.Load([AbilityTestData.Game(AbilityTestData.Circle(201, radius: 3f))]);
        return (a, b);
    }

    /// <summary>
    /// End to end (#164): Mending Circle heals the ally inside it and leaves the hostile creature beside it
    /// untouched, and the ally's new health reaches the ally and a watcher.
    /// </summary>
    [Fact]
    public void Heal_the_ally_in_a_mending_circle_and_leave_the_hostile_untouched()
    {
        using MapInstance instance = BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient healer = Join(instance, 164_231);
        MapInstanceClient ally = Join(instance, 164_232);
        MapInstanceClient watcher = Join(instance, 164_233);
        healer.Character.Spells.Load([AbilityTestData.Game(AbilityTestData.HealCircle(232, reach: 15f, radius: 4f))]);
        ally.Character.Position = new Vector3(0f, 0f, 8f);
        ally.Character.Health = 100;
        ally.Character.CurrentHealth = 40;
        watcher.Character.Position = new Vector3(0f, 0f, -10f);
        Creature hostile = AddCreature(instance, 164_994, new Vector3(1f, 0f, 8f), health: 50, currentHealth: 20);
        TickUntilBroadcast(instance);
        ally.Sent.Clear();
        watcher.Sent.Clear();

        handler.Execute(healer.Connection,
            new CCastAbilityPacket { AbilityId = 232, GroundPos = new Vector3Dto { X = 0f, Y = 0f, Z = 8f } });
        TickUntilBroadcast(instance);

        Assert.Empty(healer.Read<SAbilityNotReadyPacket>(NetworkPacketType.SMSG_ABILITY_NOT_READY));
        Assert.Equal(80u, ally.Character.CurrentHealth);
        Assert.Equal(20u, hostile.CurrentHealth);
        Assert.Contains(ally.StateUpdates(), s => s.Guid == ally.Character.Guid.RawValue && s.CurrentHealth == 80u);
        Assert.Contains(watcher.StateUpdates(), s => s.Guid == ally.Character.Guid.RawValue && s.CurrentHealth == 80u);
    }

    /// <summary>
    /// A projectile that finished just before the last character left is dropped while the instance is
    /// empty (#164), because there is nobody to send its final state to. The next character to enter
    /// never sees it, frozen where it stopped.
    /// </summary>
    [Fact]
    public void Drop_a_finished_projectile_when_the_instance_empties_so_the_next_player_never_sees_it()
    {
        using MapInstance instance = BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient caster = Join(instance, 164_241);
        Creature creature = AddCreature(instance, 164_995, new Vector3(0f, 0f, 1f), health: 50);   // point blank
        caster.Character.Spells.Load([AbilityTestData.Game(AbilityTestData.Projectile(210, reach: 5f, speed: 20f))]);
        TickUntilBroadcast(instance);   // a broadcast has just gone out, so the next tick sends nothing

        handler.Execute(caster.Connection,
            new CCastAbilityPacket { AbilityId = 210, GroundPos = new Vector3Dto { X = 0f, Y = 0f, Z = 5f } });
        for (int i = 0; i < 3; i++)
        {
            instance.Update(s_tick);   // the projectile hits and finishes, three ticks short of the next broadcast
        }

        Assert.Equal(40u, creature.CurrentHealth);
        instance.RemoveCharacter(caster.Connection);
        instance.Update(s_tick);   // nobody is here
        MapInstanceClient next = Join(instance, 164_242);
        for (int i = 0; i < 30; i++)
        {
            instance.Update(s_tick);
        }

        Assert.DoesNotContain(next.Added(), s => new ObjectGuid(s.Guid).Type == ObjectType.SpellProjectile);
        Assert.DoesNotContain(next.StateUpdates(), s => new ObjectGuid(s.Guid).Type == ObjectType.SpellProjectile);
    }

    /// <summary>
    /// A character that leaves an instance mid-cast (a portal, or a respawn at town) takes no cast with
    /// it (#164): the old instance may never tick again once it is empty, so its queue cannot be what
    /// clears Casting. The cast is interrupted as it leaves, and the next instance takes a new cast.
    /// </summary>
    [Fact]
    public void Interrupt_a_cast_in_progress_when_the_caster_leaves_so_the_next_instance_takes_a_new_one()
    {
        using MapInstance first = BuildCasting(out CastAbilityHandler firstHandler);
        using MapInstance second = BuildCasting(out CastAbilityHandler secondHandler);
        MapInstanceClient wizard = Join(first, 164_251);
        MapInstanceClient watcher = Join(first, 164_252);
        wizard.Character.Spells.Load([
            AbilityTestData.Game(Timed(AbilityTestData.AimedCircle(211, reach: 18f, radius: 3f), castTimeMs: 600)),
            AbilityTestData.Game(AbilityTestData.Circle(201, radius: 3f)),
        ]);
        firstHandler.Execute(wizard.Connection,
            new CCastAbilityPacket { AbilityId = 211, GroundPos = new Vector3Dto { X = 0f, Y = 0f, Z = 5f } });
        Assert.True(wizard.Character.Spells.IsCasting);

        first.RemoveCharacter(wizard.Connection);
        wizard.Character.InstanceId = second.InstanceId;
        second.AddCharacter(wizard.Connection);

        Assert.False(wizard.Character.Spells.IsCasting);
        SCharacterInterruptedCastPacket interrupt = Assert.Single(
            watcher.Read<SCharacterInterruptedCastPacket>(NetworkPacketType.SMSG_INTERRUPTED_CAST));
        Assert.Equal(211u, interrupt.AbilityId);
        wizard.Character.LastCastStartTime = DateTime.UtcNow.AddSeconds(-1);   // past the global cooldown
        secondHandler.Execute(wizard.Connection, new CCastAbilityPacket { AbilityId = 201 });
        Assert.Empty(wizard.Read<SAbilityNotReadyPacket>(NetworkPacketType.SMSG_ABILITY_NOT_READY));
        Assert.Single(wizard.Read<SAbilityFiredPacket>(NetworkPacketType.SMSG_ABILITY_FIRED), f => f.AbilityId == 201u);
    }

    private static T Decode<T>(OutboundPacket packet)
    {
        using var stream = new MemoryStream(packet.PayloadMemory.ToArray());
        return Serializer.Deserialize<T>(stream);
    }
}
