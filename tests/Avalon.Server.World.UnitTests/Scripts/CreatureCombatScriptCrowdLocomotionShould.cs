using System.Linq;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Creatures;
using Avalon.World.Creatures.Locomotion;
using Avalon.World.Maps.Navigation;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Units;
using Avalon.World.Scripts.Creatures;
using DotRecast.Detour;
using DotRecast.Recast.Geom;
using DotRecast.Recast.Toolset.Builder;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Scripts;

/// <summary>
/// This is the defect the ruling-14 fix exists for, reproduced at the level it actually lives at:
/// several real <see cref="CreatureCombatScript"/>s sharing one real <see cref="CrowdLocomotion"/>.
/// Every gating test in <see cref="CreatureCombatScriptShould"/> mocks
/// <see cref="ICreatureLocomotion"/>, which can assert what HasArrived was told to return but can
/// never expose a wrong arrival tolerance — the bug only exists in the gap between what a real
/// locomotion's arrival actually means and what CreatureCombatScript assumed it meant.
/// </summary>
/// <remarks>
/// A single unobstructed agent walking straight in does not reliably reproduce the defect: with
/// nothing pushing it sideways it converges close enough to its exact slot that even the old
/// hardcoded +0.15f tolerance covered it (measured, not assumed — see the fix's report). Several
/// creatures actually converging on one target — the scenario CrowdLocomotion was built for and the
/// one the original bug report describes — is what makes DtCrowd's separation hold each agent off
/// its own slot by an amount (measured here at ~1.85-1.89m from the target's centre, against the old
/// 1.65m threshold) that reliably exceeds the old hardcoded tolerance, which is what this test
/// drives.
/// </remarks>
public class CreatureCombatScriptCrowdLocomotionShould
{
    /// <summary>
    /// A flat 40x40 ground quad baked with the production settings, entirely in memory — same
    /// approach as CrowdLocomotionShould.BakeFlatGround, reused here rather than shared via a
    /// production-code helper: baking a navmesh is test fixture setup, not something the
    /// locomotion classes themselves need to expose.
    /// </summary>
    private static readonly Lazy<DtNavMesh> FlatNavMesh = new(BakeFlatGround, isThreadSafe: true);

    private static DtNavMesh BakeFlatGround()
    {
        float[] vertices =
        [
            -20f, 0f, -20f,
            -20f, 0f, 20f,
            20f, 0f, 20f,
            20f, 0f, -20f,
        ];
        int[] faces = [0, 1, 2, 0, 2, 3];

        var geom = new RcSampleInputGeomProvider(vertices, faces);
        var result = new TileNavMeshBuilder().Build(geom, NavmeshBuildSettings.Create());
        Assert.NotNull(result?.NavMesh);
        return result!.NavMesh;
    }

    [Fact]
    public void Reach_Their_Claimed_Slots_And_Attack_Under_CrowdLocomotion()
    {
        var locomotion = new CrowdLocomotion(FlatNavMesh.Value, NavmeshBuildSettings.AgentRadius,
            NullLogger.Instance);

        // Production default: MapInstance registers every creature with the same agent radius it
        // built the crowd with (GameConfiguration.CreatureAgentRadius, which itself defaults to
        // NavmeshBuildSettings.AgentRadius). Using anything smaller here would dodge the exact
        // scenario this test exists to pin.
        const float agentRadius = NavmeshBuildSettings.AgentRadius;

        var targetPosition = Vector3.zero;
        ICharacter target = Substitute.For<ICharacter>();
        target.Guid.Returns(new ObjectGuid(ObjectType.Character, 100));
        target.Position.Returns(targetPosition);
        target.IsDead.Returns(false);

        var combat = Substitute.For<ICombatService>();
        combat.GetEncounterFor(Arg.Any<IUnit>()).Returns((IEncounter?)null);
        var attackedCreatures = new HashSet<ObjectGuid>();
        combat.When(c => c.ApplyDamage(Arg.Any<IUnit>(), Arg.Any<IUnit>(), Arg.Any<uint>()))
            .Do(call => attackedCreatures.Add(call.ArgAt<IUnit>(0).Guid));

        // Production default MeleeSlotRadius (== AttackRange), same as every other combat-script
        // test in this suite.
        var meleeSlots = new MeleeSlots(slotCount: 6, radius: 1.5f);

        var context = Substitute.For<ISimulationContext>();
        context.CombatService.Returns(combat);
        context.Locomotion.Returns(locomotion);
        context.MeleeSlots.Returns(meleeSlots);

        // Four creatures approaching from roughly the same direction (+X, slightly fanned in Z) —
        // the same shape CreatureCombatScriptShould.Keep_Several_Chasers_Of_One_Target_At_Least_
        // One_Agent_Diameter_Apart drives against WaypointLocomotion. Under crowd separation, each
        // one is held off its own claimed slot by a real amount — this is what actually exercises
        // Context.Locomotion.ArrivalTolerance rather than relying on a solo agent's unobstructed
        // approach, which converges close enough that the old hardcoded +0.15f tolerance covered it
        // by coincidence.
        Vector3[] starts =
        [
            new(10f, 0f, 0f),
            new(10f, 0f, 0.3f),
            new(10f, 0f, -0.3f),
            new(10f, 0f, 0.6f),
        ];

        var creatures = new List<ICreature>();
        var scripts = new List<CreatureCombatScript>();

        foreach (Vector3 start in starts)
        {
            ICreature creature = Substitute.For<ICreature>();
            creature.Guid.Returns(new ObjectGuid(ObjectType.Creature, (uint)(creatures.Count + 1)));
            creature.TauntedBy = null;
            creature.TauntExpiresAt = DateTime.MinValue;
            var metadata = Substitute.For<ICreatureMetadata>();
            metadata.SpeedRun.Returns(4f);
            creature.Metadata.Returns(metadata);
            creature.Speed.Returns(4f);
            creature.Position.Returns(start);

            // MapInstance.AddCreature's job, done manually since there is no MapInstance here.
            locomotion.Register(creature, radius: agentRadius);

            var script = new CreatureCombatScript(NullLoggerFactory.Instance, creature, context);
            script.OnEnteredRange(target); // _initialPosition = start, State = Combat

            creatures.Add(creature);
            scripts.Add(script);
        }

        // 8.5m-ish walk under crowd separation, with generous headroom (3000 ticks = 50s
        // simulated); the loop exits the moment every creature has landed at least one hit.
        TimeSpan dt = TimeSpan.FromSeconds(1d / 60d);
        for (int tick = 0; tick < 3000 && attackedCreatures.Count < creatures.Count; tick++)
        {
            foreach (CreatureCombatScript script in scripts)
                script.Update(dt); // AI scripts tick first...

            locomotion.Update(dt); // ...then locomotion, same as MapInstance.Update.
        }

        if (attackedCreatures.Count != creatures.Count)
        {
            string diag = string.Join(" | ", creatures.Select((c, i) =>
                $"c{i} pos={c.Position} distToTarget={Vector3.Distance(c.Position, targetPosition)} " +
                $"arrived={locomotion.HasArrived(c)} tol={locomotion.ArrivalTolerance(c)} " +
                $"attacked={attackedCreatures.Contains(c.Guid)}"));
            Assert.Fail("Not every chaser attacked its target under CrowdLocomotion — " +
                         $"{attackedCreatures.Count} of {creatures.Count} did. This is the arrival-" +
                         "tolerance/AttackRange gap this fix closes: a creature held off its slot " +
                         $"by crowd separation never gets judged close enough to attack. {diag}");
        }

        foreach (ICreature creature in creatures)
            combat.Received().ApplyDamage(creature, target, 10u);
    }
}
