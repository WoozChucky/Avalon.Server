using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.World;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.Server.World.UnitTests.Scripts;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using NSubstitute;
using Xunit;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>
/// An ability script's life in a real MapInstance, through the real cast handler: a leaving caster
/// takes its scripts with it (#541), a script that throws is contained and interrupted (#530), and a
/// dead caster's dropped cast is interrupted out loud (#530). Character ids 541_1xx and creature ids
/// 541_9xx are unique to this class; the ability ids are the templates'.
/// </summary>
public class MapInstanceScriptLifecycleShould
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1d / 60d);

    private static void Ticks(MapInstance instance, int count)
    {
        for (int i = 0; i < count; i++)
        {
            instance.Update(Tick);
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
        var script = Substitute.For<AiScript>(creature, Substitute.For<ISimulationContext>());
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

    /// <summary>Another caster's projectile in flight at the same time still lands (#541).</summary>
    [Fact]
    public void Keep_another_casters_projectile_when_one_caster_leaves()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient leaving = Join(instance, 541_111);
        MapInstanceClient staying = Join(instance, 541_112);
        staying.Character.Position = new Vector3(6f, 0f, 0f);
        (_, AiScript leavingTarget) = AddCreature(instance, 541_911, new Vector3(0f, 0f, 10f));
        (_, AiScript stayingTarget) = AddCreature(instance, 541_912, new Vector3(6f, 0f, 10f));
        leaving.Character.Spells.Load([AbilityTestData.Game(SlowProjectile(210))]);
        staying.Character.Spells.Load([AbilityTestData.Game(SlowProjectile(210))]);

        handler.Execute(leaving.Connection, CastAt(210, 0f, 10f));
        handler.Execute(staying.Connection, CastAt(210, 6f, 10f));
        Ticks(instance, 7);
        Assert.Equal(2, staying.Added().Count(s => IsProjectile(s.Guid)));

        instance.RemoveCharacter(leaving.Connection);
        Ticks(instance, 180);

        leavingTarget.DidNotReceive().OnHit(Arg.Any<IUnit>(), Arg.Any<uint>());
        stayingTarget.Received(1).OnHit(staying.Character, 10u);
    }

    // ── #530 part 1 ──

    /// <summary>
    /// A script whose Update throws is dropped, its removal broadcast and its caster's cast interrupted
    /// out loud (#530); another caster's projectile ticking in the same update still lands, and the
    /// instance keeps ticking.
    /// </summary>
    [Fact]
    public void Contain_a_script_whose_Update_throws_and_keep_the_instance_ticking()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler,
            extraScripts: typeof(UpdateThrowingAbilityScript));
        MapInstanceClient failing = Join(instance, 541_121);
        MapInstanceClient other = Join(instance, 541_122);
        MapInstanceClient watcher = Join(instance, 541_123);
        (_, AiScript target) = AddCreature(instance, 541_921, new Vector3(0f, 0f, 10f));
        AbilityTemplate broken = SlowProjectile(541);
        broken.SpellScript = nameof(UpdateThrowingAbilityScript);
        failing.Character.Spells.Load([AbilityTestData.Game(broken)]);
        other.Character.Spells.Load([AbilityTestData.Game(SlowProjectile(210))]);

        handler.Execute(failing.Connection, CastAt(541, 0f, 10f));   // first, so it throws ahead of the other
        handler.Execute(other.Connection, CastAt(210, 0f, 10f));
        Ticks(instance, 7);
        List<ulong> seen = watcher.Added().Where(s => IsProjectile(s.Guid)).Select(s => s.Guid).ToList();
        Assert.Equal(2, seen.Count);

        Ticks(instance, 180);   // the broken script throws on its tenth update

        SCharacterInterruptedCastPacket interrupt = Assert.Single(
            watcher.Read<SCharacterInterruptedCastPacket>(NetworkPacketType.SMSG_INTERRUPTED_CAST));
        Assert.Equal(failing.Character.Guid.RawValue, interrupt.Caster);
        Assert.Equal(541u, interrupt.AbilityId);
        Assert.Single(failing.Read<SCharacterInterruptedCastPacket>(NetworkPacketType.SMSG_INTERRUPTED_CAST));
        target.Received(1).OnHit(other.Character, 10u);
        foreach (ulong guid in seen)
        {
            Assert.Single(watcher.Removed(), g => g == guid);
        }
    }

    /// <summary>
    /// A cast-time script whose Prepare throws when the cast completes is contained on the queued path
    /// (#530): the cast is interrupted out loud, Casting is clear, and the caster casts again.
    /// </summary>
    [Fact]
    public void Contain_a_queued_script_whose_Prepare_throws_and_let_the_caster_cast_again()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler,
            extraScripts: typeof(PrepareThrowingAbilityScript));
        MapInstanceClient wizard = Join(instance, 541_131);
        MapInstanceClient watcher = Join(instance, 541_132);
        AbilityTemplate broken = AbilityTestData.AimedCircle(542);
        broken.SpellScript = nameof(PrepareThrowingAbilityScript);
        broken.CastTime = 100;
        wizard.Character.Spells.Load([
            AbilityTestData.Game(broken),
            AbilityTestData.Game(AbilityTestData.Circle(201)),
        ]);

        handler.Execute(wizard.Connection, CastAt(542, 0f, 5f));
        Assert.True(wizard.Character.Spells.IsCasting);
        Ticks(instance, 12);

        SCharacterInterruptedCastPacket interrupt = Assert.Single(
            watcher.Read<SCharacterInterruptedCastPacket>(NetworkPacketType.SMSG_INTERRUPTED_CAST));
        Assert.Equal(542u, interrupt.AbilityId);
        Assert.False(wizard.Character.Spells.IsCasting);
        wizard.Character.LastCastStartTime = DateTime.UtcNow.AddSeconds(-1);   // past the global cooldown
        handler.Execute(wizard.Connection, new CCastAbilityPacket { AbilityId = 201 });
        Assert.Single(wizard.Read<SAbilityFiredPacket>(NetworkPacketType.SMSG_ABILITY_FIRED), f => f.AbilityId == 201u);
    }

    // ── #530 part 2 ──

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
