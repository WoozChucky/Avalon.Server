using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Domain.World;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.State;
using Avalon.Network.Packets.World;
using Avalon.Server.World.UnitTests.Combat;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.World.Abilities;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Units;
using NSubstitute;
using ProtoBuf;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Abilities;

/// <summary>
/// Lag compensation (#1082), through the real cast handler, cast system and hit query: a player's instant cone is judged
/// against the units where that player saw them, the world tick its cast names, within the rewind cap. Character ids
/// 1082_1xx, creature ids 1082_9xx.
/// </summary>
public class LagCompensationShould
{
    private const uint ConeId = 200;
    private const uint SelfHealId = 201;
    private const uint ProjectileId = 202;
    private const long Start = 1_000;

    /// <summary>The cast's apex is the caster at the origin, facing +Z; the creature stands 6 m along it at the start.</summary>
    private static readonly Vector3 s_seen = new(0f, 0f, 6f);

    [Theory]
    [InlineData(0L, 1_000L, 0)]       // not sent: no rewind
    [InlineData(990L, 1_000L, 10)]
    [InlineData(900L, 1_000L, 15)]    // older than the cap: rewound only to the cap
    [InlineData(1_005L, 1_000L, 0)]   // ahead of the server: no rewind
    public void Clamp_the_rewind_to_the_cap(long viewTick, long nowTick, int expected) =>
        Assert.Equal(expected, LagCompensation.RewindTicks(viewTick, nowTick));

    [Fact]
    public void Turn_a_round_trip_into_whole_ticks_one_way() =>
        Assert.Equal(2, LagCompensation.OneWayTicks(TimeSpan.FromMilliseconds(60).Ticks));

    /// <summary>
    /// A creature walks sideways out of a narrow cone for 12 ticks. A cone cast at where it stood at the start, naming
    /// that tick, hits it; the same cast naming no tick is judged on the present, where the creature has left the cone.
    /// A creature running 3 m a tick, 36 m out by the cast, is still found where it stood: the grid box a rewound query
    /// visits is widened by the steps the instance recorded, however long, not by a fixed speed.
    /// </summary>
    [Theory]
    [InlineData(Start, 0.2f, true)]
    [InlineData(0L, 0.2f, false)]
    [InlineData(Start, 3f, true)]
    public void Hit_a_moving_creature_where_the_caster_saw_it(long viewTick, float step, bool hit)
    {
        var ticks = new WorldTicks(Start);
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler, worldTicks: ticks);
        MapInstanceClient warrior = Warrior(instance, 1082_101);
        (Creature walker, CombatResolutionShould.CountingWoundScript wounds) = AddCreature(instance, 1082_901);

        WalkOut(instance, ticks, step, walker);
        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = ConeId, ViewTick = viewTick });

        Assert.Equal(hit ? 1 : 0, wounds.Hits);
    }

    /// <summary>
    /// Rewind moves positions only: the rewound hit query does not find a creature that died after the tick the caster saw
    /// where it stood, while it finds one that walked the same way alive.
    /// </summary>
    [Fact]
    public void Never_hit_a_creature_that_died_after_the_tick_the_caster_saw()
    {
        var ticks = new WorldTicks(Start);
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler _, worldTicks: ticks);
        MapInstanceClient warrior = Warrior(instance, 1082_111);
        (Creature dead, _) = AddCreature(instance, 1082_911);
        (Creature alive, _) = AddCreature(instance, 1082_912);

        WalkOut(instance, ticks, 0.2f, dead, alive);
        dead.CurrentHealth = 0;
        var arena = (IAbilityArena)instance;
        arena.HitRewindTicks = LagCompensation.RewindTicks(Start, ticks.Completed);
        arena.HitRewindExempt = warrior.Character;
        try
        {
            IReadOnlyList<IUnit> hit =
                instance.Hits.InCone(Vector3.zero, new Vector3(0f, 0f, 1f), reach: 10f, arcDegrees: 10f);

            Assert.DoesNotContain(dead, hit);
            Assert.Contains(alive, hit);
        }
        finally
        {
            arena.HitRewindTicks = 0;
            arena.HitRewindExempt = null;
        }
    }

    /// <summary>
    /// The caster is judged where it stands now, never rewound (#1082): its own inputs reach the server in order before
    /// its cast. A caster that walked 2.4 m in the 12 ticks it names is still inside its own 1 m heal circle.
    /// </summary>
    [Fact]
    public void Keep_the_caster_where_it_stands_when_it_is_its_own_target()
    {
        var ticks = new WorldTicks(Start);
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler, worldTicks: ticks);
        MapInstanceClient healer = Warrior(instance, 1082_121);
        AbilityTemplate selfHeal = AbilityTestData.Circle(SelfHealId, radius: 1f);
        selfHeal.Affects = AbilityAffects.Ally;
        selfHeal.Effects = SpellEffect.Heal;
        selfHeal.EffectValue = 40;
        healer.Character.Spells.Load([AbilityTestData.Game(selfHeal)]);
        healer.Character.Health = 100;
        healer.Character.CurrentHealth = 50;

        Tick(instance, ticks);
        for (int i = 1; i <= 12; i++)
        {
            healer.Character.Position = new Vector3(0.2f * i, 0f, 0f);
            Tick(instance, ticks);
        }

        handler.Execute(healer.Connection, new CCastAbilityPacket { AbilityId = SelfHealId, ViewTick = Start });

        Assert.True(healer.Character.CurrentHealth > 50, $"the caster has {healer.Character.CurrentHealth} health");
    }

    /// <summary>
    /// A player's projectile sweeps the units where that player saw them beside it (#1082): its own rewind, the cast's 12
    /// ticks less the caster's 2-tick one way (a 60 ms round trip), so 10. A creature walks diagonally across the lane,
    /// 0.4 m a tick sideways and 0.1 m along it; the shot naming tick <see cref="Start" /> meets it on its fourth step,
    /// where it stood 10 ticks before, and stops at the point of that step nearest that position (it was 4 m to the side
    /// and 1 m further along by then). Rewound by the cast's whole 12 ticks it would miss, and naming no tick it passes
    /// behind the creature.
    /// </summary>
    [Theory]
    [InlineData(Start, true)]
    [InlineData(0L, false)]
    public void Sweep_a_projectile_against_a_moving_creature_where_the_caster_saw_it(long viewTick, bool hit)
    {
        var ticks = new WorldTicks(Start);
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler, worldTicks: ticks);
        MapInstanceClient archer = Warrior(instance, 1082_131);
        archer.Character.Spells.Load([AbilityTestData.Game(AbilityTestData.Projectile(ProjectileId, reach: 20f, speed: 60f))]);
        archer.Connection.RoundTripTime.Returns(TimeSpan.FromMilliseconds(60).Ticks);
        (Creature walker, CombatResolutionShould.CountingWoundScript wounds) = AddCreature(instance, 1082_931);

        // Ticks 1000 to 1011 before the cast, taken on tick 1012; one step of 1 m a tick, on ticks 1012 to 1031.
        while (ticks.Completed < Start + 12)
            Walk(instance, ticks, walker);
        handler.Execute(archer.Connection, new CCastAbilityPacket
        {
            AbilityId = ProjectileId,
            GroundPos = new Vector3Dto { X = 0f, Y = 0f, Z = 20f },
            ViewTick = viewTick,
        });
        for (int i = 0; i < 24; i++)
            Walk(instance, ticks, walker);

        Assert.Equal(hit ? 1 : 0, wounds.Hits);
        if (hit)
        {
            // The fourth step runs from 3 m to 4 m; the creature stood at (0, 3.5) ten ticks before it.
            ObjectState last = LastProjectileState(archer);
            Assert.Equal(0f, last.Position!.X, 2);
            Assert.Equal(3.5f, last.Position.Z, 2);
        }
    }

    /// <summary>
    /// One tick of the projectile test's creature: it stands on its diagonal path, at (0, 3.5) on tick 1005 (where the
    /// fourth step of the shot, on tick 1015, sees it 10 ticks back), then the instance ticks.
    /// </summary>
    private static void Walk(MapInstance instance, WorldTicks ticks, Creature creature)
    {
        float t = ticks.Completed - (Start + 5);
        creature.Position = new Vector3(0.4f * t, 0f, 3.5f + 0.1f * t);
        Tick(instance, ticks);
    }

    /// <summary>The last state <paramref name="client" /> was sent for a projectile with a position: where it stopped.</summary>
    private static ObjectState LastProjectileState(MapInstanceClient client)
    {
        ObjectState? last = null;
        foreach (OutboundPacket packet in client.Sent)
        {
            List<ObjectState>? states = packet.Header.Type switch
            {
                NetworkPacketType.SMSG_WORLD_STATE_ADD => Decode<SInstanceStateAddPacket>(packet).Adds,
                NetworkPacketType.SMSG_WORLD_STATE_UPDATE => Decode<SInstanceStateUpdatePacket>(packet).Updates,
                _ => null,
            };
            foreach (ObjectState state in states ?? [])
            {
                if (new ObjectGuid(state.Guid).Type == ObjectType.SpellProjectile && state.Position is not null)
                    last = state;
            }
        }

        Assert.NotNull(last);
        return last;
    }

    private static T Decode<T>(OutboundPacket packet)
    {
        using var stream = new MemoryStream(packet.PayloadMemory.ToArray());
        return Serializer.Deserialize<T>(stream);
    }

    /// <summary>
    /// One tick with every creature at <see cref="s_seen" /> (tick <see cref="Start" />), then 12 ticks of each walking
    /// <paramref name="step" /> a tick along +X, out of the cone: at the cast (tick 1013), at 0.2 m a tick, they stand
    /// 2.4 m to the side.
    /// </summary>
    private static void WalkOut(MapInstance instance, WorldTicks ticks, float step, params Creature[] creatures)
    {
        Tick(instance, ticks);
        for (int i = 1; i <= 12; i++)
        {
            foreach (Creature creature in creatures)
                creature.Position = s_seen + new Vector3(step * i, 0f, 0f);
            Tick(instance, ticks);
        }
    }

    private static void Tick(MapInstance instance, WorldTicks ticks)
    {
        instance.Update(TimeSpan.FromSeconds(1d / 60d));
        ticks.Advance();
    }

    /// <summary>A Warrior at the origin facing +Z, with a 10 m cone 10 degrees wide.</summary>
    private static MapInstanceClient Warrior(MapInstance instance, uint id)
    {
        MapInstanceClient warrior = Join(instance, id);
        warrior.Character.Position = Vector3.zero;
        warrior.Character.Orientation = Vector3.zero;
        AbilityTemplate cone = AbilityTestData.Cone(ConeId, reach: 10f, arc: 10f);
        warrior.Character.Spells.Load([AbilityTestData.Game(cone)]);
        return warrior;
    }

    private static (Creature, CombatResolutionShould.CountingWoundScript) AddCreature(MapInstance instance, uint id)
    {
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, id),
            Metadata = Loot.LootTestData.BoarTemplate(null),
            Position = s_seen,
            Health = 100,
            CurrentHealth = 100,
            Level = 1,
        };
        var wounds = new CombatResolutionShould.CountingWoundScript(creature);
        creature.Script = wounds;
        instance.AddCreature(creature);
        return (creature, wounds);
    }
}
