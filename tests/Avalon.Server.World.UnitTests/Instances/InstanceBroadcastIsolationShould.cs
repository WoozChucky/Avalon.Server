using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Maps;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Scripts.Creatures;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>
/// #546: a unit's broadcasts, hits, kills and departures go through the instance it is in, never
/// through a static event every live instance hears.
/// </summary>
public class InstanceBroadcastIsolationShould
{
    [Fact]
    public void Leave_A_Creature_In_Another_Instance_Engaged_When_Its_Target_Leaves_This_One()
    {
        using MapInstance left = Build();
        using MapInstance other = Build();
        MapInstanceClient leaver = Join(left, 546_001);
        (Creature creature, CreatureCombatScript script) = EngagedCreature(other, 546_901, leaver.Character);

        left.RemoveCharacter(leaver.Connection);

        Assert.Equal(CreatureCombatScript.CombatState.Combat, script.State);
        Assert.Equal(50u, creature.CurrentHealth);
    }

    [Fact]
    public void Send_A_Creature_Home_When_Its_Target_Leaves_Its_Own_Instance()
    {
        using MapInstance instance = Build();
        MapInstanceClient leaver = Join(instance, 546_002);
        (Creature creature, CreatureCombatScript script) = EngagedCreature(instance, 546_902, leaver.Character);

        instance.RemoveCharacter(leaver.Connection);

        Assert.Equal(CreatureCombatScript.CombatState.Returning, script.State);
        Assert.Equal(creature.Health, creature.CurrentHealth);
    }

    /// <summary>
    /// One script that throws as a character leaves stops neither the others hearing it nor the
    /// removal itself.
    /// </summary>
    [Fact]
    public void Tell_Every_Other_Script_And_Still_Remove_The_Character_When_One_Script_Throws()
    {
        using MapInstance instance = Build();
        MapInstanceClient leaver = Join(instance, 546_003);
        var broken = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, 546_903),
            Metadata = Substitute.For<ICreatureMetadata>(),
        };
        broken.Script = new Avalon.Server.World.UnitTests.Scripts.ThrowOnLeaveScript(broken, instance);
        instance.AddCreature(broken);
        (_, CreatureCombatScript script) = EngagedCreature(instance, 546_904, leaver.Character);

        instance.RemoveCharacter(leaver.Connection);

        Assert.Equal(CreatureCombatScript.CombatState.Returning, script.State);
        Assert.DoesNotContain(leaver.Character.Guid, instance.Characters.Keys);
    }

    /// <summary>
    /// A creature's hit on a character is sent exactly as it was when the static events carried it: the
    /// character's own damage packet first, then the hit to everyone in its instance, and nothing to
    /// anyone in another.
    /// </summary>
    [Fact]
    public void Send_A_Characters_Hit_To_Its_Own_Instance_Only()
    {
        using MapInstance own = Build();
        using MapInstance other = Build();
        MapInstanceClient wounded = Join(own, 546_004);
        wounded.Character.Health = 100;
        wounded.Character.CurrentHealth = 100;
        MapInstanceClient watcher = Join(own, 546_005);
        MapInstanceClient elsewhere = Join(other, 546_006);
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, 546_905),
            Metadata = Substitute.For<ICreatureMetadata>(),
        };
        own.AddCreature(creature);

        own.CombatService.ApplyDamage(creature, wounded.Character, 7);

        Assert.Equal([NetworkPacketType.SMSG_CHARACTER_DAMAGED, NetworkPacketType.SMSG_CREATURE_DAMAGED],
            wounded.Sent.Select(p => p.Header.Type));
        SCharacterDamagePacket self = Assert.Single(wounded.Read<SCharacterDamagePacket>(NetworkPacketType.SMSG_CHARACTER_DAMAGED));
        Assert.Equal((creature.Guid.RawValue, wounded.Character.Guid.RawValue, 93u, 7u, (uint?)null),
            (self.Attacker, self.Target, self.CurrentHealth, self.Damage, self.AbilityId));
        SUnitDamagePacket hit = Assert.Single(watcher.Read<SUnitDamagePacket>(NetworkPacketType.SMSG_CREATURE_DAMAGED));
        Assert.Equal((creature.Guid.RawValue, wounded.Character.Guid.RawValue, 93u, 7u),
            (hit.Attacker, hit.Target, hit.CurrentHealth, hit.Damage));
        Assert.Empty(watcher.Read<SCharacterDamagePacket>(NetworkPacketType.SMSG_CHARACTER_DAMAGED));
        Assert.Empty(elsewhere.Sent);
    }

    /// <summary>
    /// A cast or swing broadcast reaches everyone in the unit's instance and nobody in another, and an
    /// instance sends none for a unit that is not in it.
    /// </summary>
    [Fact]
    public void Send_Cast_And_Swing_Broadcasts_To_The_Units_Own_Instance_Only()
    {
        using MapInstance own = Build();
        using MapInstance other = Build();
        MapInstanceClient caster = Join(own, 546_007);
        MapInstanceClient elsewhere = Join(other, 546_008);
        var ability = Substitute.For<IAbility>();
        ability.AbilityId.Returns(new AbilityId(546));
        ability.Metadata.Returns(new AbilityMetadata { AnimationId = 3u });

        own.BroadcastFinishCast(caster.Character, ability);
        own.BroadcastInterruptedCast(caster.Character, ability);
        own.BroadcastAttackAnimation(caster.Character, ability);
        other.BroadcastFinishCast(caster.Character, ability);
        other.BroadcastInterruptedCast(caster.Character, ability);
        other.BroadcastAttackAnimation(caster.Character, null);

        Assert.Equal(
            [NetworkPacketType.SMSG_UNIT_FINISH_CAST, NetworkPacketType.SMSG_INTERRUPTED_CAST,
                NetworkPacketType.SMSG_CREATURE_ATTACK_ANIMATION],
            caster.Sent.Select(p => p.Header.Type));
        Assert.Empty(elsewhere.Sent);
    }

    /// <summary>An instance whose navigator walks straight to wherever it is asked.</summary>
    private static MapInstance Build()
    {
        var navigator = Substitute.For<IMapNavigator>();
        navigator.FindPath(Arg.Any<Vector3>(), Arg.Any<Vector3>())
            .Returns(ci => new List<Vector3> { ci.ArgAt<Vector3>(1) });
        return TestMapInstances.Build(NewWorld(), navigator: navigator);
    }

    /// <summary>A wounded creature with a real combat script, engaged on <paramref name="target" />.</summary>
    private static (Creature, CreatureCombatScript) EngagedCreature(MapInstance instance, uint id,
        ICharacter target)
    {
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, id),
            Metadata = Substitute.For<ICreatureMetadata>(),
            Position = new Vector3(3f, 0f, 3f),
            Health = 100,
            CurrentHealth = 50,
        };
        instance.AddCreature(creature);
        var script = new CreatureCombatScript(NullLoggerFactory.Instance, creature, instance);
        creature.Script = script;
        script.OnEnteredRange(target);
        return (creature, script);
    }
}
