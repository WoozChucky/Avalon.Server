using System.IO;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.State;
using Avalon.Network.Packets.World;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.World;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Public.Combat;
using Avalon.World.Scripts;
using Avalon.World.Scripts.Abilities;
using Microsoft.Extensions.Logging.Abstractions;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Instances;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Maps;
using Avalon.World.Public.Scripts;
using NSubstitute;
using ProtoBuf;
using Xunit;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>Casting through a real MapInstance. Character ids are unique to this class (164_1xx).</summary>
public class MapInstanceAbilityCastShould
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1d / 60d);

    /// <summary>Seven 60 Hz ticks: past the 0.1 s state broadcast interval.</summary>
    private static void TickUntilBroadcast(MapInstance instance)
    {
        for (int i = 0; i < 7; i++)
        {
            instance.Update(Tick);
        }
    }

    private static IAbility HealAbility()
    {
        var ability = Substitute.For<IAbility>();
        ability.AbilityId.Returns(new AbilityId(232));
        ability.Metadata.Returns(new AbilityMetadata { Name = "Heal", ScriptName = "x" });
        return ability;
    }

    private static IAbility DamageAbility(uint id)
    {
        var ability = Substitute.For<IAbility>();
        ability.AbilityId.Returns(new AbilityId(id));
        ability.Metadata.Returns(new AbilityMetadata { Name = "Strike", ScriptName = "x" });
        return ability;
    }

    /// <summary>The heal path (#164): a healed character's health rides the entity state update, to itself too.</summary>
    [Fact]
    public void Send_a_heals_health_change_in_the_next_state_update()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        MapInstanceClient healed = Join(instance, 164_111);
        MapInstanceClient watcher = Join(instance, 164_112);
        healed.Character.Health = 100;
        healed.Character.CurrentHealth = 40;
        TickUntilBroadcast(instance);
        healed.Sent.Clear();
        watcher.Sent.Clear();

        instance.CombatService.ApplyHeal(watcher.Character, healed.Character, 30, HealAbility());
        TickUntilBroadcast(instance);

        Assert.Contains(watcher.StateUpdates(), s => s.Guid == healed.Character.Guid.RawValue && s.CurrentHealth == 70u);
        Assert.Contains(healed.StateUpdates(), s => s.Guid == healed.Character.Guid.RawValue && s.CurrentHealth == 70u);
    }

    /// <summary>#521 item 8: the victim learns which ability hit it.</summary>
    [Fact]
    public void Name_the_ability_on_the_victims_damage_packet()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        MapInstanceClient victim = Join(instance, 164_121);
        victim.Character.Health = 100;
        victim.Character.CurrentHealth = 100;
        var attacker = Substitute.For<ICreature>();
        attacker.Guid.Returns(new ObjectGuid(ObjectType.Creature, 164_900u));

        instance.CombatService.ApplyDamage(attacker, victim.Character, 10, DamageAbility(id: 211));

        SCharacterDamagePacket damage = Assert.Single(victim.Read<SCharacterDamagePacket>(NetworkPacketType.SMSG_CHARACTER_DAMAGED));
        Assert.Equal(211u, damage.AbilityId);
        Assert.Equal(90u, damage.CurrentHealth);
    }

    /// <summary>A swing names no ability (#521 item 8).</summary>
    [Fact]
    public void Name_no_ability_on_a_swings_damage_packet()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        MapInstanceClient victim = Join(instance, 164_131);
        victim.Character.Health = 100;
        victim.Character.CurrentHealth = 100;
        var attacker = Substitute.For<ICreature>();
        attacker.Guid.Returns(new ObjectGuid(ObjectType.Creature, 164_901u));

        instance.CombatService.ApplyDamage(attacker, victim.Character, 10);

        SCharacterDamagePacket damage = Assert.Single(victim.Read<SCharacterDamagePacket>(NetworkPacketType.SMSG_CHARACTER_DAMAGED));
        Assert.Null(damage.AbilityId);
        Assert.Equal(90u, damage.CurrentHealth);
    }

    /// <summary>#521 item 9: other clients learn which ability a unit is casting, not just for how long.</summary>
    [Fact]
    public void Name_the_ability_on_the_start_cast_broadcast()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        MapInstanceClient caster = Join(instance, 164_101);
        MapInstanceClient watcher = Join(instance, 164_102);
        var ability = Substitute.For<IAbility>();
        ability.AbilityId.Returns(new AbilityId(211));
        ability.Metadata.Returns(new AbilityMetadata { Name = "Flame Burst", ScriptName = "x", CastTime = 0.6f });

        instance.BroadcastUnitStartCast(caster.Character, ability);

        foreach (MapInstanceClient client in new[] { caster, watcher })
        {
            SUnitStartCastPacket start = Assert.Single(client.Read<SUnitStartCastPacket>(NetworkPacketType.SMSG_UNIT_START_CAST));
            Assert.Equal(caster.Character.Guid.RawValue, start.Caster);
            Assert.Equal(211u, start.AbilityId);
            Assert.Equal(0.6f, start.CastTime);
        }
    }

    [Fact]
    public void Carry_the_ability_id_through_a_protobuf_round_trip()
    {
        var original = new SUnitStartCastPacket { Caster = 42UL, CastTime = 1.25f, AbilityId = 232 };
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, original);
        stream.Position = 0;

        SUnitStartCastPacket decoded = Serializer.Deserialize<SUnitStartCastPacket>(stream);

        Assert.Equal(232u, decoded.AbilityId);
        Assert.Equal(42UL, decoded.Caster);
        Assert.Equal(1.25f, decoded.CastTime);
    }

    /// <summary>The handler builds the shape script by name and the instance tells every client where it fired (#164).</summary>
    [Fact]
    public void Fire_a_circle_through_the_handler_and_tell_everyone_where()
    {
        var scripts = Substitute.For<IScriptManager>();
        scripts.GetAbilityScript(nameof(CircleAbilityScript)).Returns(typeof(CircleAbilityScript));
        IWorld world = NewWorld();
        using MapInstance instance = TestMapInstances.Build(world, scripts);
        world.InstanceRegistry.GetInstanceById(instance.InstanceId).Returns(instance);
        MapInstanceClient caster = Join(instance, 164_141);
        MapInstanceClient watcher = Join(instance, 164_142);
        watcher.Character.Health = 100;
        watcher.Character.CurrentHealth = 100;
        Assert.False(watcher.Character.IsDead);
        Assert.Equal(caster.Character.Position, watcher.Character.Position);   // inside the circle
        caster.Character.Spells.Load([AbilityTestData.Game(AbilityTestData.Circle(201, radius: 3f))]);
        var handler = new CastAbilityHandler(NullLogger<CastAbilityHandler>.Instance, world, new CombatConfig());

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

    /// <summary>A projectile is a world object (#164): it enters every client's view, moves, and leaves it when it ends.</summary>
    [Fact]
    public void Spawn_move_and_despawn_a_projectile_through_the_world_object_path()
    {
        var scripts = Substitute.For<IScriptManager>();
        scripts.GetAbilityScript(nameof(ProjectileAbilityScript)).Returns(typeof(ProjectileAbilityScript));
        var navigator = Substitute.For<IMapNavigator>();
        navigator.RaycastWalkable(default, default).ReturnsForAnyArgs(ci => ci.ArgAt<Vector3>(1));
        IWorld world = NewWorld();
        using MapInstance instance = TestMapInstances.Build(world, scripts, navigator);
        world.InstanceRegistry.GetInstanceById(instance.InstanceId).Returns(instance);
        MapInstanceClient caster = Join(instance, 164_151);
        MapInstanceClient watcher = Join(instance, 164_152);
        caster.Character.Spells.Load([AbilityTestData.Game(AbilityTestData.Projectile(210, reach: 5f, speed: 20f))]);
        var handler = new CastAbilityHandler(NullLogger<CastAbilityHandler>.Instance, world, new CombatConfig());

        handler.Execute(caster.Connection,
            new CCastAbilityPacket { AbilityId = 210, GroundPos = new Vector3Dto { X = 0f, Y = 0f, Z = 5f } });
        for (int i = 0; i < 30; i++)
        {
            instance.Update(Tick);
        }

        Assert.Empty(caster.Read<SAbilityNotReadyPacket>(NetworkPacketType.SMSG_ABILITY_NOT_READY));
        ObjectState projectile = Assert.Single(watcher.Added(), s => new ObjectGuid(s.Guid).Type == ObjectType.SpellProjectile);
        Assert.Contains(projectile.Guid, watcher.Removed());
    }

    /// <summary>
    /// A point-blank projectile ends on the tick after it spawns, between two state broadcasts. Clients
    /// still see it (#164): one add, then its final state (zero velocity) on the next broadcast, then
    /// one remove, in that order; and the creature is hit once.
    /// </summary>
    [Fact]
    public void Show_a_point_blank_projectile_spawn_its_final_state_and_its_despawn_in_order()
    {
        var scripts = Substitute.For<IScriptManager>();
        scripts.GetAbilityScript(nameof(ProjectileAbilityScript)).Returns(typeof(ProjectileAbilityScript));
        var navigator = Substitute.For<IMapNavigator>();
        navigator.RaycastWalkable(default, default).ReturnsForAnyArgs(ci => ci.ArgAt<Vector3>(1));
        IWorld world = NewWorld();
        using MapInstance instance = TestMapInstances.Build(world, scripts, navigator);
        world.InstanceRegistry.GetInstanceById(instance.InstanceId).Returns(instance);
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
        var ai = Substitute.For<AiScript>(creature, Substitute.For<ISimulationContext>());
        creature.Script = ai;   // a creature takes its damage through its script
        instance.AddCreature(creature);
        caster.Character.Spells.Load([AbilityTestData.Game(AbilityTestData.Projectile(210, reach: 5f, speed: 20f))]);
        var handler = new CastAbilityHandler(NullLogger<CastAbilityHandler>.Instance, world, new CombatConfig());

        handler.Execute(caster.Connection,
            new CCastAbilityPacket { AbilityId = 210, GroundPos = new Vector3Dto { X = 0f, Y = 0f, Z = 5f } });
        for (int i = 0; i < 30; i++)
        {
            instance.Update(Tick);
        }

        List<(string Kind, ObjectState? State)> seen = [];
        foreach (NetworkPacket packet in watcher.Sent)
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

    private static T Decode<T>(NetworkPacket packet)
    {
        using var stream = new MemoryStream(packet.Payload);
        return Serializer.Deserialize<T>(stream);
    }
}
