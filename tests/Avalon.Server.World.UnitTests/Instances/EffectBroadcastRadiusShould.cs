using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abstractions;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Creatures;
using NSubstitute;
using Xunit;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>
/// #532: a one-shot effect broadcast goes only to connections within Game:EffectBroadcastRadius of the
/// effect on X/Z, or whose character is involved in it. The default radius is 60 m.
/// </summary>
public class EffectBroadcastRadiusShould
{
    private const float Radius = 60f;

    private static readonly Vector3 Inside = new(30f, 0f, 30f);
    private static readonly Vector3 Outside = new(100f, 0f, 0f);
    private static readonly Vector3 Far = new(500f, 0f, 500f);

    [Fact]
    public void Default_the_radius_to_60() => Assert.Equal(60f, new GameConfiguration().EffectBroadcastRadius);

    // ---- Each method: one watcher inside, one outside --------------------------------------------

    [Fact]
    public void Send_a_unit_hit_only_to_the_watcher_inside_the_radius()
    {
        using MapInstance instance = Build();
        (MapInstanceClient near, MapInstanceClient far) = Watchers(instance);
        Creature attacker = AddCreature(instance, 1, Vector3.zero);
        Creature target = AddCreature(instance, 2, new Vector3(1f, 0f, 0f));

        instance.BroadcastUnitHit(attacker, target, 10, 5);

        AssertOnlyNear(near, far, NetworkPacketType.SMSG_CREATURE_DAMAGED);
    }

    [Fact]
    public void Send_a_start_cast_only_to_the_watcher_inside_the_radius()
    {
        using MapInstance instance = Build();
        (MapInstanceClient near, MapInstanceClient far) = Watchers(instance);
        Creature caster = AddCreature(instance, 1, Vector3.zero);

        instance.BroadcastUnitStartCast(caster, Ability());

        AssertOnlyNear(near, far, NetworkPacketType.SMSG_UNIT_START_CAST);
    }

    [Fact]
    public void Send_a_finish_cast_only_to_the_watcher_inside_the_radius()
    {
        using MapInstance instance = Build();
        (MapInstanceClient near, MapInstanceClient far) = Watchers(instance);
        Creature caster = AddCreature(instance, 1, Vector3.zero);

        instance.BroadcastFinishCast(caster, Ability());

        AssertOnlyNear(near, far, NetworkPacketType.SMSG_UNIT_FINISH_CAST);
    }

    [Fact]
    public void Send_an_interrupted_cast_only_to_the_watcher_inside_the_radius()
    {
        using MapInstance instance = Build();
        (MapInstanceClient near, MapInstanceClient far) = Watchers(instance);
        Creature caster = AddCreature(instance, 1, Vector3.zero);

        instance.BroadcastInterruptedCast(caster, Ability());

        AssertOnlyNear(near, far, NetworkPacketType.SMSG_INTERRUPTED_CAST);
    }

    [Fact]
    public void Send_an_attack_animation_only_to_the_watcher_inside_the_radius()
    {
        using MapInstance instance = Build();
        (MapInstanceClient near, MapInstanceClient far) = Watchers(instance);
        Creature attacker = AddCreature(instance, 1, Vector3.zero);

        instance.BroadcastAttackAnimation(attacker, null);

        AssertOnlyNear(near, far, NetworkPacketType.SMSG_CREATURE_ATTACK_ANIMATION);
    }

    [Fact]
    public void Send_an_ability_fired_only_to_the_watcher_inside_the_radius()
    {
        using MapInstance instance = Build();
        (MapInstanceClient near, MapInstanceClient far) = Watchers(instance);
        Creature caster = AddCreature(instance, 1, Vector3.zero);

        instance.BroadcastAbilityFired(caster, Ability(), Vector3.zero, new Vector3(0f, 0f, 1f), null);

        AssertOnlyNear(near, far, NetworkPacketType.SMSG_ABILITY_FIRED);
    }

    [Fact]
    public void Send_a_death_only_to_the_watcher_inside_the_radius()
    {
        using MapInstance instance = Build();
        (MapInstanceClient near, MapInstanceClient far) = Watchers(instance);
        Creature unit = AddCreature(instance, 1, Vector3.zero);

        instance.BroadcastUnitDeath(unit, null);

        AssertOnlyNear(near, far, NetworkPacketType.SMSG_UNIT_DEATH);
    }

    [Fact]
    public void Send_a_revive_only_to_the_watcher_inside_the_radius_of_where_the_unit_revives()
    {
        using MapInstance instance = Build();
        (MapInstanceClient near, MapInstanceClient far) = Watchers(instance);
        Creature unit = AddCreature(instance, 1, Far);   // where it died is not where it revives

        instance.BroadcastUnitRevive(unit, Vector3.zero, 100);

        AssertOnlyNear(near, far, NetworkPacketType.SMSG_UNIT_REVIVE);
    }

    // ---- Involved units beyond the radius still hear it ------------------------------------------

    [Fact]
    public void Send_a_hit_to_the_attacker_and_the_target_even_when_both_are_beyond_the_radius()
    {
        using MapInstance instance = Build();
        MapInstanceClient attacker = JoinAt(instance, 10, Far);
        MapInstanceClient target = JoinAt(instance, 11, -Far);
        MapInstanceClient bystander = JoinAt(instance, 12, Vector3.zero);

        instance.BroadcastUnitHit(attacker.Character, target.Character, 10, 5);

        Assert.Equal([NetworkPacketType.SMSG_CREATURE_DAMAGED], attacker.Sent.Select(p => p.Header.Type));
        Assert.Equal([NetworkPacketType.SMSG_CREATURE_DAMAGED], target.Sent.Select(p => p.Header.Type));
        Assert.Empty(bystander.Sent);
    }

    [Fact]
    public void Send_a_hit_to_a_watcher_near_the_target_though_far_from_the_attacker()
    {
        using MapInstance instance = Build();
        Creature attacker = AddCreature(instance, 1, Far);
        Creature target = AddCreature(instance, 2, Vector3.zero);
        MapInstanceClient nearTarget = JoinAt(instance, 10, Inside);

        instance.BroadcastUnitHit(attacker, target, 10, 5);

        Assert.Single(nearTarget.Sent);
    }

    [Fact]
    public void Send_cast_broadcasts_to_the_caster_whatever_its_position()
    {
        using MapInstance instance = Build();
        MapInstanceClient caster = JoinAt(instance, 10, Vector3.zero);
        // The caster's position is unreadable: only involvement can reach it.
        caster.Character.Position = new Vector3(float.NaN, 0f, float.NaN);
        IAbility ability = Ability();

        instance.BroadcastUnitStartCast(caster.Character, ability);
        instance.BroadcastFinishCast(caster.Character, ability);
        instance.BroadcastInterruptedCast(caster.Character, ability);
        instance.BroadcastAttackAnimation(caster.Character, ability);
        instance.BroadcastAbilityFired(caster.Character, ability, Far, null, Far);

        Assert.Equal(
            [NetworkPacketType.SMSG_UNIT_START_CAST, NetworkPacketType.SMSG_UNIT_FINISH_CAST,
                NetworkPacketType.SMSG_INTERRUPTED_CAST, NetworkPacketType.SMSG_CREATURE_ATTACK_ANIMATION,
                NetworkPacketType.SMSG_ABILITY_FIRED],
            caster.Sent.Select(p => p.Header.Type));
    }

    [Fact]
    public void Send_a_death_to_the_killer_beyond_the_radius()
    {
        using MapInstance instance = Build();
        Creature unit = AddCreature(instance, 1, Vector3.zero);
        MapInstanceClient killer = JoinAt(instance, 10, Far);

        instance.BroadcastUnitDeath(unit, killer.Character);

        Assert.Single(killer.Sent);
    }

    [Fact]
    public void Send_a_death_to_the_dying_character_whatever_its_position()
    {
        using MapInstance instance = Build();
        MapInstanceClient dying = JoinAt(instance, 10, Vector3.zero);
        dying.Character.Position = new Vector3(float.PositiveInfinity, 0f, 0f);

        instance.BroadcastUnitDeath(dying.Character, null);

        Assert.Single(dying.Sent);
    }

    [Fact]
    public void Send_a_revive_to_the_revived_character_though_it_revives_beyond_the_radius()
    {
        using MapInstance instance = Build();
        MapInstanceClient revived = JoinAt(instance, 10, Vector3.zero);   // still standing where it died

        instance.BroadcastUnitRevive(revived.Character, Far, 100);

        Assert.Single(revived.Sent);
    }

    // ---- The rule's edges ------------------------------------------------------------------------

    [Fact]
    public void Send_to_a_watcher_exactly_at_the_radius_but_not_just_past_it()
    {
        using MapInstance instance = Build();
        Creature unit = AddCreature(instance, 1, Vector3.zero);
        MapInstanceClient atEdge = JoinAt(instance, 10, new Vector3(Radius, 0f, 0f));
        MapInstanceClient past = JoinAt(instance, 11, new Vector3(0f, 0f, Radius + 0.01f));

        instance.BroadcastUnitDeath(unit, null);

        Assert.Single(atEdge.Sent);
        Assert.Empty(past.Sent);
    }

    [Fact]
    public void Send_an_ability_fired_to_a_watcher_near_its_cursor_centre_though_far_from_its_origin()
    {
        using MapInstance instance = Build();
        Creature caster = AddCreature(instance, 1, Vector3.zero);
        MapInstanceClient nearCentre = JoinAt(instance, 10, Far + new Vector3(5f, 0f, 0f));
        MapInstanceClient nowhere = JoinAt(instance, 11, -Far);

        instance.BroadcastAbilityFired(caster, Ability(), Vector3.zero, null, Far);

        Assert.Single(nearCentre.Sent);
        Assert.Empty(nowhere.Sent);
    }

    [Fact]
    public void Not_exclude_a_watcher_whose_distance_is_only_along_y()
    {
        using MapInstance instance = Build();
        Creature unit = AddCreature(instance, 1, Vector3.zero);
        MapInstanceClient above = JoinAt(instance, 10, new Vector3(0f, 1000f, 0f));

        instance.BroadcastUnitDeath(unit, null);

        Assert.Single(above.Sent);
    }

    [Fact]
    public void Count_a_dead_watcher_by_its_position()
    {
        using MapInstance instance = Build();
        Creature unit = AddCreature(instance, 1, Vector3.zero);
        MapInstanceClient dead = JoinAt(instance, 10, Inside);
        dead.Character.CurrentHealth = 0;

        instance.BroadcastUnitDeath(unit, null);

        Assert.Single(dead.Sent);
    }

    [Theory]
    [InlineData(float.NaN, 0f, 0f)]
    [InlineData(0f, 0f, float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity, 0f, 0f)]
    [InlineData(0f, float.NaN, 0f)]
    public void Never_count_a_non_finite_watcher_position_as_near(float x, float y, float z)
    {
        using MapInstance instance = Build();
        Creature unit = AddCreature(instance, 1, Vector3.zero);
        MapInstanceClient watcher = JoinAt(instance, 10, new Vector3(x, y, z));

        instance.BroadcastUnitDeath(unit, null);

        Assert.Empty(watcher.Sent);
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
        instance.BroadcastAbilityFired(unit, Ability(), point, null, null);

        Assert.Empty(watcher.Sent);
    }

    [Fact]
    public void Use_the_configured_radius()
    {
        using MapInstance instance = Build(new GameConfiguration { EffectBroadcastRadius = 5f });
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

    private static (MapInstanceClient Near, MapInstanceClient Far) Watchers(MapInstance instance) =>
        (JoinAt(instance, 532_001, Inside), JoinAt(instance, 532_002, Outside));

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
        var ability = Substitute.For<IAbility>();
        ability.AbilityId.Returns(new AbilityId(532));
        ability.Metadata.Returns(new AbilityMetadata { AnimationId = 3u, CastTime = 1000f });
        return ability;
    }

    private static void AssertOnlyNear(MapInstanceClient near, MapInstanceClient far, NetworkPacketType type)
    {
        Assert.Equal([type], near.Sent.Select(p => p.Header.Type));
        Assert.Empty(far.Sent);
    }
}
