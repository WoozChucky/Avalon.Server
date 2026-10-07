using System.Reflection;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Network.Packets.State;
using Avalon.World.Creatures.Locomotion;
using Avalon.World.Maps.Navigation;
using Avalon.World.Public.Creatures;
using DotRecast.Detour;
using DotRecast.Detour.Crowd;
using DotRecast.Recast.Geom;
using DotRecast.Recast.Toolset.Builder;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Creatures;

/// <summary>
/// The crowd implementation has to be swappable with <see cref="WaypointLocomotion" /> by
/// configuration alone, so these tests cover the contract the two share: agent lifecycle, coming to
/// rest, teleport, and what HasArrived means. Steering itself is deliberately not asserted — a test
/// that pinned a trajectory would encode DotRecast internals and break on any upgrade, which is why
/// the design leaves steering to be judged by eye behind the feature flag.
/// </summary>
public class CrowdLocomotionShould
{
    /// <summary>
    /// A flat 40x40 ground quad baked with the production settings, entirely in memory: nothing is
    /// read from disk and no external service is touched. Baked once because the bake is the
    /// expensive part and a <see cref="DtCrowd" /> is built per test anyway. Internal (not private)
    /// so <c>MapInstanceLocomotionShould</c> can bake a <see cref="CrowdLocomotion" /> over the same
    /// mesh without duplicating this bake.
    /// </summary>
    internal static readonly Lazy<DtNavMesh> s_flatNavMesh = new(BakeFlatGround, isThreadSafe: true);

    private static DtNavMesh BakeFlatGround()
    {
        float[] vertices =
        [
            -20f, 0f, -20f,
            -20f, 0f, 20f,
            20f, 0f, 20f,
            20f, 0f, -20f,
        ];
        // Counter-clockwise when seen from above, so both triangles get an upward normal and pass
        // the walkable-slope filter.
        int[] faces = [0, 1, 2, 0, 2, 3];

        var geom = new RcSampleInputGeomProvider(vertices, faces);
        NavMeshBuildResult result = new TileNavMeshBuilder().Build(geom, NavmeshBuildSettings.Create());
        Assert.NotNull(result?.NavMesh);
        return result!.NavMesh;
    }

    private static (CrowdLocomotion Locomotion, DtCrowd Crowd) BuildOverAFlatNavMesh()
    {
        var locomotion = new CrowdLocomotion(s_flatNavMesh.Value, NavmeshBuildSettings.AgentRadius,
            NullLogger.Instance);
        return (locomotion, CrowdOf(locomotion));
    }

    /// <summary>
    /// The crowd is an implementation detail with only a read-only accessor, but "did an agent
    /// actually reach DtCrowd" is the only honest way to test registration — a count of the class's own dictionary
    /// would pass even if AddAgent were never called.
    /// </summary>
    private static DtCrowd CrowdOf(CrowdLocomotion locomotion) => locomotion.Crowd;

    /// <summary>
    /// The agents <see cref="CrowdLocomotion.Update" />'s position/velocity copy-back enumerates, one per
    /// registered creature. Read by reflection, so the steering test can follow a creature's own agent.
    /// </summary>
    private static Dictionary<ObjectGuid, DtCrowdAgent> CreatureAgentsOf(CrowdLocomotion locomotion) =>
        (Dictionary<ObjectGuid, DtCrowdAgent>)typeof(CrowdLocomotion)
            .GetField("_creatureAgents", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(locomotion)!;

    private static ICreature CreatureAt(Vector3 position)
    {
        ICreature creature = Substitute.For<ICreature>();
        creature.Guid.Returns(new ObjectGuid(ObjectType.Creature, 1));
        creature.Position.Returns(position);
        creature.Speed.Returns(NavmeshBuildSettings.AgentMaxSpeed);
        return creature;
    }

    /// <summary>A player's ObjectGuid is a Character guid, distinct from every CreatureAt guid above.</summary>
    private static readonly ObjectGuid s_playerGuid = new(ObjectType.Character, 500);

    /// <summary>
    /// The counterpart to WaypointLocomotionShould's equivalent test: unlike WaypointLocomotion
    /// (before its own fix), CrowdLocomotion's Register already no-ops on an already-registered
    /// creature, leaving whatever move request is in progress untouched rather than removing and
    /// re-adding the agent (which would also discard the pending move request — see Teleport's
    /// remarks on why remove/re-add clears it). Re-entrant registration must not turn a chasing
    /// creature into a stuck one. While it walks, the locomotion owns only the at-rest transition;
    /// the caller (a script) owns the moving MoveState and must not have it overwritten every tick.
    /// </summary>
    [Fact]
    public void Keep_An_In_Progress_Move_When_Registered_Again_And_Leave_The_Moving_MoveState_To_The_Caller()
    {
        (CrowdLocomotion locomotion, DtCrowd crowd) = BuildOverAFlatNavMesh();
        ICreature creature = CreatureAt(Vector3.zero);

        locomotion.Register(creature, radius: 0.5f);
        locomotion.MoveTo(creature, new Vector3(15f, 0f, 15f));
        Assert.False(locomotion.HasArrived(creature));

        // Re-entrant Register while the move request above is still pending.
        locomotion.Register(creature, radius: 0.5f);

        Assert.Single(crowd.GetActiveAgents());
        Assert.False(locomotion.HasArrived(creature));

        for (int i = 0; i < 5; i++)
            locomotion.Update(TimeSpan.FromSeconds(1d / 60d));

        Assert.False(locomotion.HasArrived(creature));
        creature.DidNotReceiveWithAnyArgs().MoveState = default;
    }

    /// <summary>
    /// Review Focus 3, crowd side: unregistering twice leaves no agent behind (a leaked one would keep
    /// steering a dead creature), and a creature with no agent is inert rather than an exception.
    /// </summary>
    [Fact]
    public void Tolerate_Unregistering_Twice_And_Treat_An_Unregistered_Creature_As_Already_Arrived()
    {
        (CrowdLocomotion locomotion, DtCrowd crowd) = BuildOverAFlatNavMesh();
        ICreature creature = CreatureAt(Vector3.zero);

        locomotion.Register(creature, radius: 0.5f);
        locomotion.Unregister(creature);
        locomotion.Unregister(creature);

        Assert.Empty(crowd.GetActiveAgents());

        locomotion.MoveTo(creature, new Vector3(3f, 0f, 0f));
        locomotion.Stop(creature);
        locomotion.Update(TimeSpan.FromSeconds(1));

        Assert.True(locomotion.HasArrived(creature));
    }

    /// <summary>
    /// The tolerance this class advertises has to actually describe its own arrival decision — the
    /// same value <c>CrowdLocomotion.Arrived</c> in the production class uses internally, not a
    /// coincidentally similar one — or a caller relying on it (CreatureCombatScript's in-range
    /// check) would under-trust how close "arrived" really means and never consider itself close
    /// enough. Registers with the default production agent radius (0.6f, larger than the 0.3f
    /// floor) so the tolerance actually being checked is radius-driven, not the floor.
    /// </summary>
    [Fact]
    public void Report_A_Tolerance_Consistent_With_Its_Own_Arrival_Decision()
    {
        (CrowdLocomotion locomotion, DtCrowd crowd) = BuildOverAFlatNavMesh();
        ICreature creature = CreatureAt(Vector3.zero);
        var destination = new Vector3(3f, 0f, 0f);

        locomotion.Register(creature, radius: NavmeshBuildSettings.AgentRadius);
        locomotion.MoveTo(creature, destination);

        // The exact point Arrived() measures against is the nearest navmesh point to
        // `destination`, not `destination` itself — FindNearestPoly can snap it by a few
        // centimetres. Capture that snapped target now, before an arrival Update resets it, so the
        // comparison below is against the same point the production arrival check itself uses
        // rather than reintroducing that snap as test noise.
        DtCrowdAgent agent = Assert.Single(crowd.GetActiveAgents());
        var actualTarget = new Vector3(agent.targetPos.X, agent.targetPos.Y, agent.targetPos.Z);

        for (int i = 0; i < 600 && !locomotion.HasArrived(creature); i++)
            locomotion.Update(TimeSpan.FromSeconds(1d / 60d));

        Assert.True(locomotion.HasArrived(creature));
        float distanceFromActualTarget = Vector3.Distance(creature.Position, actualTarget);
        float tolerance = locomotion.ArrivalTolerance(creature);
        Assert.Equal(NavmeshBuildSettings.AgentRadius, tolerance); // radius (0.6) exceeds the 0.3 floor
        Assert.True(distanceFromActualTarget <= tolerance,
            $"reported arrived {distanceFromActualTarget}m from its actual target but advertises " +
            $"only {tolerance}m tolerance");
    }

    /// <summary>
    /// Review Focus 2, crowd side. A destination with no polygon under it is the crowd's equivalent
    /// of WaypointLocomotion's empty path: it must come to rest rather than throw, and must not
    /// leave MoveState at a moving value with nowhere to walk.
    /// </summary>
    [Fact]
    public void Come_To_Rest_When_The_Destination_Is_Off_The_Navmesh()
    {
        (CrowdLocomotion locomotion, _) = BuildOverAFlatNavMesh();
        ICreature creature = CreatureAt(Vector3.zero);

        locomotion.Register(creature, radius: 0.5f);
        locomotion.MoveTo(creature, new Vector3(5000f, 0f, 5000f));

        creature.Received().MoveState = MoveState.Idle;
        creature.Received().Velocity = Vector3.zero;
        Assert.True(locomotion.HasArrived(creature));
    }

    /// <summary>
    /// A leash-return teleport must discard whatever destination was pending: otherwise the crowd
    /// would steer the creature straight back along the route it was teleported off.
    /// </summary>
    [Fact]
    public void Discard_A_Pending_Destination_When_Teleported()
    {
        (CrowdLocomotion locomotion, DtCrowd crowd) = BuildOverAFlatNavMesh();
        ICreature creature = CreatureAt(Vector3.zero);
        var destination = new Vector3(12f, 0f, -8f);

        locomotion.Register(creature, radius: 0.5f);
        locomotion.MoveTo(creature, new Vector3(-15f, 0f, 15f));
        locomotion.Teleport(creature, destination);
        locomotion.Update(TimeSpan.FromSeconds(1));

        DtCrowdAgent agent = Assert.Single(crowd.GetActiveAgents());
        Assert.Equal(DtMoveRequestState.DT_CROWDAGENT_TARGET_NONE, agent.targetState);
        Assert.True(MathF.Abs(agent.npos.X - destination.x) < 1f, $"agent X was {agent.npos.X}");
        Assert.True(locomotion.HasArrived(creature));
    }

    // --- Task 9: player agents ------------------------------------------------------------------
    //
    // Players are told to the crowd, never asked — PlayerInputHandler is the only authority on
    // where a player is. A player agent exists purely so creatures can see and avoid it; it must
    // never be steered, and it must never feed a position back to anything.

    /// <summary>
    /// A player agent cannot move itself. The server already decided where the player is: the crowd is
    /// told, never asked, so whatever Integrate and HandleCollisions compute for that agent is
    /// overwritten by the next sync. Review Focus, player side: disconnects race the per-tick sync, so a
    /// second removal must not throw.
    /// </summary>
    [Fact]
    public void Keep_A_Player_Agent_Where_It_Is_Told_From_Sync_To_Removal()
    {
        (CrowdLocomotion locomotion, DtCrowd crowd) = BuildOverAFlatNavMesh();

        locomotion.SyncPlayer(s_playerGuid, new Vector3(1f, 0f, 1f));

        DtCrowdAgent agent = Assert.Single(crowd.GetActiveAgents());
        Assert.Equal(0f, agent.option.maxSpeed);
        Assert.Equal(0f, agent.option.maxAcceleration);

        locomotion.Update(TimeSpan.FromSeconds(0.1));
        locomotion.SyncPlayer(s_playerGuid, new Vector3(5f, 0f, 5f));

        DtCrowdAgent moved = Assert.Single(crowd.GetActiveAgents());
        Assert.InRange(moved.npos.X, 4.9f, 5.1f);

        locomotion.RemovePlayer(s_playerGuid);
        locomotion.RemovePlayer(s_playerGuid);

        Assert.Empty(crowd.GetActiveAgents());
    }

    /// <summary>
    /// F3. Speed is <see cref="ICreature.Speed" />, read fresh every tick — not a value frozen when
    /// the agent was created. This is the one place the two implementations used to genuinely
    /// disagree: <see cref="WaypointLocomotion" /> reads <c>creature.Speed</c> every tick in
    /// <c>Advance</c>, while this class used to bake the speed handed to <c>Register</c> into
    /// <c>DtCrowdAgentParams</c> and never look again. That mattered because the calling scripts set
    /// the speed <em>after</em> registration and change it during the creature's life —
    /// <c>CreatureCombatScript</c> sets <c>SpeedRun</c>, <c>CreaturePatrolScript</c> sets
    /// <c>SpeedWalk</c> — so a creature switching between walking and running behaved differently
    /// depending on which implementation the configuration flag selected. Registering slow and then
    /// speeding up is the direction that is unambiguous to observe: ground covered.
    /// Production change that breaks this: dropping the <c>agent.option.maxSpeed = moving.Speed</c>
    /// refresh loop at the top of <see cref="CrowdLocomotion.Update" />, which pins the agent to the
    /// 0.5 it was registered with (~0.5 units of travel in the second below, not ~2.5).
    /// </summary>
    [Fact]
    public void Follow_A_Speed_Change_Made_After_Registration()
    {
        (CrowdLocomotion locomotion, _) = BuildOverAFlatNavMesh();
        ICreature creature = CreatureAt(Vector3.zero);
        creature.Speed.Returns(0.5f);

        locomotion.Register(creature, radius: 0.5f);
        locomotion.MoveTo(creature, new Vector3(15f, 0f, 0f));

        // The script's decision, made after Register — exactly as CreatureCombatScript's
        // `Creature.Speed = Creature.Metadata.SpeedRun` is made after MapInstance.AddCreature.
        creature.Speed.Returns(3.5f);

        for (int tick = 0; tick < 60; tick++)
            locomotion.Update(TimeSpan.FromSeconds(1d / 60d));

        // One simulated second. At the registered 0.5 m/s the agent cannot have passed 0.5 units;
        // at the creature's actual 3.5 m/s it covers most of 3.5, minus the acceleration ramp. 1.5
        // sits clear of both, so this cannot pass on ramp-up noise alone.
        Assert.True(creature.Position.x > 1.5f,
            $"creature only reached x={creature.Position.x} in one second — it is still moving at the " +
            "speed it was registered with, not the speed it actually has.");
    }

    /// <summary>
    /// A creature routed straight through a stationary player must steer around it rather than
    /// walking through it — the whole point of registering players as obstacles. Runs the real
    /// DotRecast obstacle-avoidance/separation simulation on a flat, open mesh: nothing else on this
    /// 20m corridor could account for a lateral deviation from a path that starts and ends on the
    /// same straight line.
    /// </summary>
    [Fact]
    public void Steer_A_Creature_Around_A_Stationary_Player_Rather_Than_Straight_Through_It()
    {
        (CrowdLocomotion locomotion, _) = BuildOverAFlatNavMesh();
        ICreature creature = CreatureAt(new Vector3(-10f, 0f, 0f));
        var destination = new Vector3(10f, 0f, 0f);
        var playerPosition = new Vector3(0f, 0f, 0f);

        locomotion.Register(creature, radius: 0.5f);
        locomotion.SyncPlayer(s_playerGuid, playerPosition);
        locomotion.MoveTo(creature, destination);

        DtCrowdAgent creatureAgent = CreatureAgentsOf(locomotion)[creature.Guid];

        // Re-sync the player every tick, exactly as MapInstance.Update does — a stationary player
        // whose position is never re-pushed would still be "stationary" for this test, but the real
        // per-tick contract is what production code actually runs.
        float maxLateralDeviation = 0f;
        for (int i = 0; i < 300 && creatureAgent.npos.X < 0f; i++)
        {
            locomotion.SyncPlayer(s_playerGuid, playerPosition);
            locomotion.Update(TimeSpan.FromSeconds(1d / 60d));
            maxLateralDeviation = MathF.Max(maxLateralDeviation, MathF.Abs(creatureAgent.npos.Z));
        }

        Assert.True(maxLateralDeviation > 0.2f,
            $"creature passed the player's X position with only {maxLateralDeviation}m of lateral " +
            "deviation from the straight line it started on");
    }
}
