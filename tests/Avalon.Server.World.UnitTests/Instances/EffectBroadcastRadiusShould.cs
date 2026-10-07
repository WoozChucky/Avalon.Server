using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.World.Abilities;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Units;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>
/// #532: a one-shot effect broadcast goes only to connections within Game:InterestRadius of the
/// effect on X/Z, or whose character is involved in it. The default radius is 60 m.
/// </summary>
public class EffectBroadcastRadiusShould
{
    private static readonly Vector3 s_inside = new(30f, 0f, 30f);
    private static readonly Vector3 s_outside = new(100f, 0f, 0f);
    private static readonly Vector3 s_far = new(500f, 0f, 500f);

    /// <summary>Not finite, so near nothing: only being involved in an effect reaches a watcher standing here.</summary>
    private static readonly Vector3 s_unplaced = new(float.NaN, 0f, float.NaN);

    // ---- Each broadcast: one watcher inside, one outside, and the units involved ---------------------

    [Theory]
    [InlineData(NetworkPacketType.SMSG_CREATURE_DAMAGED)]
    [InlineData(NetworkPacketType.SMSG_UNIT_START_CAST)]
    [InlineData(NetworkPacketType.SMSG_UNIT_FINISH_CAST)]
    [InlineData(NetworkPacketType.SMSG_INTERRUPTED_CAST)]
    [InlineData(NetworkPacketType.SMSG_CREATURE_ATTACK_ANIMATION)]
    [InlineData(NetworkPacketType.SMSG_ABILITY_FIRED)]
    [InlineData(NetworkPacketType.SMSG_UNIT_DEATH)]
    [InlineData(NetworkPacketType.SMSG_UNIT_REVIVE)]
    public void Send_an_effect_only_to_the_watcher_inside_the_radius(NetworkPacketType effect)
    {
        using MapInstance instance = Build();
        MapInstanceClient near = JoinAt(instance, 532_001, s_inside);
        MapInstanceClient far = JoinAt(instance, 532_002, s_outside);
        // A revive is heard where the unit revives (the origin), not where it died.
        Creature unit = AddCreature(instance, 1, effect == NetworkPacketType.SMSG_UNIT_REVIVE ? s_far : Vector3.zero);
        Creature other = AddCreature(instance, 2, new Vector3(1f, 0f, 0f));

        Broadcast(instance, effect, unit, other);

        Assert.Equal([effect], near.Sent.Select(p => p.Header.Type));
        Assert.Empty(far.Sent);
    }

    /// <summary>
    /// The units an effect involves hear it wherever they stand: a hit's attacker and target, the caster of a
    /// cast, a swing or a fired ability, the dying unit and its killer, the revived unit.
    /// </summary>
    [Theory]
    [InlineData(NetworkPacketType.SMSG_CREATURE_DAMAGED, true)]
    [InlineData(NetworkPacketType.SMSG_UNIT_START_CAST, false)]
    [InlineData(NetworkPacketType.SMSG_UNIT_FINISH_CAST, false)]
    [InlineData(NetworkPacketType.SMSG_INTERRUPTED_CAST, false)]
    [InlineData(NetworkPacketType.SMSG_CREATURE_ATTACK_ANIMATION, false)]
    [InlineData(NetworkPacketType.SMSG_ABILITY_FIRED, false)]
    [InlineData(NetworkPacketType.SMSG_UNIT_DEATH, true)]
    [InlineData(NetworkPacketType.SMSG_UNIT_REVIVE, false)]
    public void Send_an_effect_to_the_units_it_involves_wherever_they_stand(NetworkPacketType effect, bool otherInvolved)
    {
        using MapInstance instance = Build();
        MapInstanceClient unit = JoinAt(instance, 532_003, s_unplaced);
        MapInstanceClient other = JoinAt(instance, 532_004, s_unplaced);

        Broadcast(instance, effect, unit.Character, other.Character);

        Assert.Equal([effect], unit.Sent.Select(p => p.Header.Type));
        NetworkPacketType[] heardByOther = otherInvolved ? [effect] : [];
        Assert.Equal(heardByOther, other.Sent.Select(p => p.Header.Type));
    }

    // ---- An effect with a second point is heard near either ---------------------------------------

    [Fact]
    public void Send_a_hit_to_a_watcher_near_the_target_though_far_from_the_attacker()
    {
        using MapInstance instance = Build();
        Creature attacker = AddCreature(instance, 1, s_far);
        Creature target = AddCreature(instance, 2, Vector3.zero);
        MapInstanceClient nearTarget = JoinAt(instance, 10, s_inside);

        instance.BroadcastUnitHit(attacker, target, 10, 5);

        Assert.Single(nearTarget.Sent);
    }

    [Fact]
    public void Send_an_ability_fired_to_a_watcher_near_its_cursor_centre_though_far_from_its_origin()
    {
        using MapInstance instance = Build();
        Creature caster = AddCreature(instance, 1, Vector3.zero);
        MapInstanceClient nearCentre = JoinAt(instance, 10, s_far + new Vector3(5f, 0f, 0f));
        MapInstanceClient nowhere = JoinAt(instance, 11, -s_far);

        instance.BroadcastAbilityFired(caster, Ability(), Fired(Vector3.zero, null, s_far));

        Assert.Single(nearCentre.Sent);
        Assert.Empty(nowhere.Sent);
    }

    // ---- The rule's edges ------------------------------------------------------------------------

    [Theory]
    [InlineData(0f, 1000f, 0f, true)]   // straight above: height is ignored
    [InlineData(float.NaN, 0f, 0f, false)]
    [InlineData(0f, 0f, float.PositiveInfinity, false)]
    [InlineData(float.NegativeInfinity, 0f, 0f, false)]
    [InlineData(0f, float.NaN, 0f, false)]
    public void Place_a_watcher_on_x_and_z_and_never_count_a_non_finite_position_as_near(float x, float y, float z,
        bool hears)
    {
        using MapInstance instance = Build();
        Creature unit = AddCreature(instance, 1, Vector3.zero);
        MapInstanceClient watcher = JoinAt(instance, 10, new Vector3(x, y, z));

        instance.BroadcastUnitDeath(unit, null);

        NetworkPacketType[] heard = hears ? [NetworkPacketType.SMSG_UNIT_DEATH] : [];
        Assert.Equal(heard, watcher.Sent.Select(p => p.Header.Type));
    }

    [Fact]
    public void Count_a_dead_watcher_by_its_position()
    {
        using MapInstance instance = Build();
        Creature unit = AddCreature(instance, 1, Vector3.zero);
        MapInstanceClient dead = JoinAt(instance, 10, s_inside);
        dead.Character.CurrentHealth = 0;

        instance.BroadcastUnitDeath(unit, null);

        Assert.Single(dead.Sent);
    }

    [Theory]
    [InlineData(float.NaN, 0f, 0f)]
    [InlineData(0f, 0f, float.PositiveInfinity)]
    [InlineData(0f, float.NaN, 0f)]
    public void Never_count_a_watcher_as_near_a_non_finite_point(float x, float y, float z)
    {
        using MapInstance instance = Build();
        var point = new Vector3(x, y, z);
        Creature unit = AddCreature(instance, 1, point);
        MapInstanceClient watcher = JoinAt(instance, 10, Vector3.zero);

        instance.BroadcastUnitDeath(unit, null);
        instance.BroadcastAbilityFired(unit, Ability(), Fired(point, null, null));

        Assert.Empty(watcher.Sent);
    }

    /// <summary>The configured radius, inclusive: 5 m away is heard, 6 m is not.</summary>
    [Fact]
    public void Use_the_configured_radius()
    {
        using MapInstance instance = Build(new GameConfiguration { InterestRadius = 5f });
        Creature unit = AddCreature(instance, 1, Vector3.zero);
        MapInstanceClient at5 = JoinAt(instance, 10, new Vector3(3f, 0f, 4f));
        MapInstanceClient at6 = JoinAt(instance, 11, new Vector3(6f, 0f, 0f));

        instance.BroadcastUnitDeath(unit, null);

        Assert.Single(at5.Sent);
        Assert.Empty(at6.Sent);
    }

    // ---- Helpers ---------------------------------------------------------------------------------

    private static MapInstance Build(GameConfiguration? configuration = null)
    {
        Avalon.World.IWorld world = NewWorld();
        if (configuration is not null)
        {
            world.Configuration.Returns(configuration);
        }

        return TestMapInstances.Build(world);
    }

    private static MapInstanceClient JoinAt(MapInstance instance, uint id, Vector3 position)
    {
        MapInstanceClient client = Join(instance, id);
        client.Character.Position = position;
        return client;
    }

    private static Creature AddCreature(MapInstance instance, uint id, Vector3 position)
    {
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, 532_900 + id),
            Metadata = Substitute.For<ICreatureMetadata>(),
            Position = position,
            Health = 100,
            CurrentHealth = 100,
        };
        instance.AddCreature(creature);
        return creature;
    }

    private static IAbility Ability()
    {
        IAbility ability = Substitute.For<IAbility>();
        ability.AbilityId.Returns(new AbilityId(532));
        ability.Metadata.Returns(new AbilityMetadata { AnimationId = 3u, CastTime = 1000f });
        return ability;
    }

    /// <summary>
    /// Makes the broadcast that sends <paramref name="effect" />, by <paramref name="unit" />. <paramref name="other" />
    /// is the hit's target and the death's killer, and takes no part in the rest. A revive brings the unit back
    /// at the origin, and an ability fires at the origin.
    /// </summary>
    private static void Broadcast(MapInstance instance, NetworkPacketType effect, IUnit unit, IUnit other)
    {
        switch (effect)
        {
            case NetworkPacketType.SMSG_CREATURE_DAMAGED:
                instance.BroadcastUnitHit(unit, other, 10, 5);
                break;
            case NetworkPacketType.SMSG_UNIT_START_CAST:
                instance.BroadcastUnitStartCast(unit, Ability(), 1u, null);
                break;
            case NetworkPacketType.SMSG_UNIT_FINISH_CAST:
                instance.BroadcastFinishCast(unit, Ability());
                break;
            case NetworkPacketType.SMSG_INTERRUPTED_CAST:
                instance.BroadcastInterruptedCast(unit, Ability());
                break;
            case NetworkPacketType.SMSG_CREATURE_ATTACK_ANIMATION:
                instance.BroadcastAttackAnimation(unit, null);
                break;
            case NetworkPacketType.SMSG_ABILITY_FIRED:
                instance.BroadcastAbilityFired(unit, Ability(), Fired(Vector3.zero, new Vector3(0f, 0f, 1f), null));
                break;
            case NetworkPacketType.SMSG_UNIT_DEATH:
                instance.BroadcastUnitDeath(unit, other);
                break;
            case NetworkPacketType.SMSG_UNIT_REVIVE:
                instance.BroadcastUnitRevive(unit, Vector3.zero, 100);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(effect), effect, "Not an effect broadcast.");
        }
    }

    /// <summary>A fired circle or cone at these points, with no dimensions: only where it is heard matters here.</summary>
    private static AbilityFootprint Fired(Vector3 origin, Vector3? direction, Vector3? centre) =>
        new(direction is null ? AbilityShape.Circle : AbilityShape.Cone, origin, direction, centre, 0f, 0f, 0f);
}
