using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Domain.World;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abilities;
using Avalon.Server.World.UnitTests.Combat;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.World.Abilities;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
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
    /// </summary>
    [Theory]
    [InlineData(Start, true)]
    [InlineData(0L, false)]
    public void Hit_a_moving_creature_where_the_caster_saw_it(long viewTick, bool hit)
    {
        var ticks = new WorldTicks(Start);
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler, worldTicks: ticks);
        MapInstanceClient warrior = Warrior(instance, 1082_101);
        (Creature walker, CombatResolutionShould.CountingWoundScript wounds) = AddCreature(instance, 1082_901);

        WalkOut(instance, ticks, walker);
        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = ConeId, ViewTick = viewTick });

        Assert.Equal(hit ? 1 : 0, wounds.Hits);
    }

    /// <summary>
    /// Rewind moves positions only: a creature that died after the tick the caster saw is not hit where it stood, while
    /// one that walked the same way alive is.
    /// </summary>
    [Fact]
    public void Never_hit_a_creature_that_died_after_the_tick_the_caster_saw()
    {
        var ticks = new WorldTicks(Start);
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler, worldTicks: ticks);
        MapInstanceClient warrior = Warrior(instance, 1082_111);
        (Creature dead, CombatResolutionShould.CountingWoundScript deadWounds) = AddCreature(instance, 1082_911);
        (Creature alive, CombatResolutionShould.CountingWoundScript aliveWounds) = AddCreature(instance, 1082_912);

        WalkOut(instance, ticks, dead, alive);
        dead.CurrentHealth = 0;
        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = ConeId, ViewTick = Start });

        Assert.Equal(0, deadWounds.Hits);
        Assert.Equal(1, aliveWounds.Hits);
    }

    /// <summary>
    /// One tick with every creature at <see cref="s_seen" /> (tick <see cref="Start" />), then 12 ticks of each walking
    /// 0.2 m a tick along +X, out of the cone: at the cast (tick 1013) they stand 2.4 m to the side.
    /// </summary>
    private static void WalkOut(MapInstance instance, WorldTicks ticks, params Creature[] creatures)
    {
        Tick(instance, ticks);
        for (int i = 1; i <= 12; i++)
        {
            foreach (Creature creature in creatures)
                creature.Position = s_seen + new Vector3(0.2f * i, 0f, 0f);
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
