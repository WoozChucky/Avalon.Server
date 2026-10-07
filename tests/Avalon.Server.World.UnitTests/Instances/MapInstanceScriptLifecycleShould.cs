using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.World;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>
/// An ability script's life in a real MapInstance, through the real cast handler: a leaving caster
/// takes its scripts with it (#541), and a dead caster's dropped cast is interrupted out loud (#530).
/// A script that throws is contained by the cast system (InstanceAbilityCastSystemContainmentShould).
/// Character ids 541_1xx and creature ids 541_9xx are unique to this class; the ability ids are the templates'.
/// </summary>
public class MapInstanceScriptLifecycleShould
{
    private static readonly TimeSpan s_tick = TimeSpan.FromSeconds(1d / 60d);

    private static void Ticks(MapInstance instance, int count)
    {
        for (int i = 0; i < count; i++)
        {
            instance.Update(s_tick);
        }
    }

    /// <summary>A real creature whose substitute script records every hit.</summary>
    private static (Creature Creature, AiScript Script) AddCreature(MapInstance instance, uint id, Vector3 position)
    {
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, id),
            Metadata = Loot.LootTestData.BoarTemplate(null),
            Position = position,
            Health = 100,
            CurrentHealth = 100,
        };
        AiScript script = Substitute.For<AiScript>(creature, Substitute.For<ISimulationContext>());
        creature.Script = script;   // a creature takes its damage through its script
        instance.AddCreature(creature);
        return (creature, script);
    }

    /// <summary>A slow projectile (5 m/s): two seconds to reach something ten metres away.</summary>
    private static AbilityTemplate SlowProjectile(uint id) => AbilityTestData.Projectile(id, reach: 20f, speed: 5f);

    private static CCastAbilityPacket CastAt(uint abilityId, float x, float z) =>
        new() { AbilityId = abilityId, GroundPos = new Vector3Dto { X = x, Y = 0f, Z = z } };

    private static bool IsProjectile(ulong guid) => new ObjectGuid(guid).Type == ObjectType.SpellProjectile;

    private static bool ThreatOn(MapInstance instance, Creature creature, IUnit unit) =>
        instance.CombatService.GetEncounterFor(creature)?.GetThreatList(creature).ContainsKey(unit) ?? false;

    // ── #541 ──

    /// <summary>
    /// A caster that leaves with a projectile in flight takes it along (#541): it never lands, the
    /// target gains no threat on the departed caster, and a watcher is told once that it is gone.
    /// </summary>
    [Fact]
    public void Drop_a_projectile_in_flight_when_its_caster_leaves()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient caster = Join(instance, 541_101);
        MapInstanceClient watcher = Join(instance, 541_102);
        (Creature creature, AiScript ai) = AddCreature(instance, 541_901, new Vector3(0f, 0f, 10f));
        caster.Character.Spells.Load([AbilityTestData.Game(SlowProjectile(210))]);

        handler.Execute(caster.Connection, CastAt(210, 0f, 10f));
        Ticks(instance, 7);   // past a state broadcast: the watcher has seen it
        ulong projectile = Assert.Single(watcher.Added(), s => IsProjectile(s.Guid)).Guid;
        Assert.DoesNotContain(projectile, watcher.Removed());

        instance.RemoveCharacter(caster.Connection);
        Ticks(instance, 180);

        ai.DidNotReceive().OnHit(Arg.Any<IUnit>(), Arg.Any<uint>());
        Assert.Equal(100u, creature.CurrentHealth);
        Assert.False(ThreatOn(instance, creature, caster.Character));
        Assert.Single(watcher.Removed(), g => g == projectile);
    }

    /// <summary>
    /// A projectile that has finished but is still held for its final broadcast when its caster
    /// leaves (#541) is dropped with it, and a watcher that saw it is told once that it is gone.
    /// </summary>
    [Fact]
    public void Remove_a_finished_projectile_held_for_its_final_broadcast_once_when_its_caster_leaves()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient caster = Join(instance, 541_151);
        MapInstanceClient watcher = Join(instance, 541_152);
        AddCreature(instance, 541_951, new Vector3(0f, 0f, 1f));   // point blank
        caster.Character.Spells.Load([AbilityTestData.Game(AbilityTestData.Projectile(210, reach: 5f, speed: 20f))]);
        Ticks(instance, 7);   // a broadcast has just gone out, so the next few ticks send no update

        handler.Execute(caster.Connection, CastAt(210, 0f, 5f));
        Ticks(instance, 3);   // it hits and finishes, its final state still owed
        ulong projectile = Assert.Single(watcher.Added(), s => IsProjectile(s.Guid)).Guid;
        Assert.DoesNotContain(projectile, watcher.Removed());

        instance.RemoveCharacter(caster.Connection);
        Ticks(instance, 30);

        Assert.Single(watcher.Removed(), g => g == projectile);
    }

    /// <summary>A projectile whose caster leaves on the tick it was fired is never shown: no add, no remove (#541).</summary>
    [Fact]
    public void Never_show_a_projectile_dropped_on_the_tick_it_spawned()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient caster = Join(instance, 541_161);
        MapInstanceClient watcher = Join(instance, 541_162);
        caster.Character.Spells.Load([AbilityTestData.Game(SlowProjectile(210))]);
        Ticks(instance, 7);

        handler.Execute(caster.Connection, CastAt(210, 0f, 10f));
        instance.RemoveCharacter(caster.Connection);
        Ticks(instance, 30);

        Assert.DoesNotContain(watcher.Added(), s => IsProjectile(s.Guid));
        Assert.DoesNotContain(watcher.Removed(), IsProjectile);
    }

    // ── #530 ──

    /// <summary>
    /// A caster that dies mid-cast has its cast dropped, and every client hears the interrupt, so no
    /// cast bar is left running (#530). Once revived, the caster is not stuck casting.
    /// </summary>
    [Fact]
    public void Interrupt_a_dead_casters_dropped_cast_out_loud_and_let_it_cast_again()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient wizard = Join(instance, 541_141);
        MapInstanceClient watcher = Join(instance, 541_142);
        AbilityTemplate timed = AbilityTestData.AimedCircle(211);
        timed.CastTime = 300;
        wizard.Character.Spells.Load([
            AbilityTestData.Game(timed),
            AbilityTestData.Game(AbilityTestData.Circle(201)),
        ]);
        handler.Execute(wizard.Connection, CastAt(211, 0f, 5f));
        Assert.True(wizard.Character.Spells.IsCasting);

        wizard.Character.IsDead = true;
        Ticks(instance, 30);

        SCharacterInterruptedCastPacket interrupt = Assert.Single(
            watcher.Read<SCharacterInterruptedCastPacket>(NetworkPacketType.SMSG_INTERRUPTED_CAST));
        Assert.Equal(wizard.Character.Guid.RawValue, interrupt.Caster);
        Assert.Equal(211u, interrupt.AbilityId);
        Assert.Empty(watcher.Read<SAbilityFiredPacket>(NetworkPacketType.SMSG_ABILITY_FIRED));
        Assert.False(wizard.Character.Spells.IsCasting);

        wizard.Character.Revive();
        wizard.Character.LastCastStartTime = DateTime.UtcNow.AddSeconds(-1);   // past the global cooldown
        handler.Execute(wizard.Connection, new CCastAbilityPacket { AbilityId = 201 });
        Assert.Empty(wizard.Read<SAbilityNotReadyPacket>(NetworkPacketType.SMSG_ABILITY_NOT_READY));
        Assert.Single(wizard.Read<SAbilityFiredPacket>(NetworkPacketType.SMSG_ABILITY_FIRED), f => f.AbilityId == 201u);
    }
}
