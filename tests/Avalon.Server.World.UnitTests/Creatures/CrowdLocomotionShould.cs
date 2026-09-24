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
using Xunit;

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
    internal static readonly Lazy<DtNavMesh> FlatNavMesh = new(BakeFlatGround, isThreadSafe: true);

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
        var result = new TileNavMeshBuilder().Build(geom, NavmeshBuildSettings.Create());
        Assert.NotNull(result?.NavMesh);
        return result!.NavMesh;
    }

    private static (CrowdLocomotion Locomotion, DtCrowd Crowd) BuildOverAFlatNavMesh()
    {
        var locomotion = new CrowdLocomotion(FlatNavMesh.Value, NavmeshBuildSettings.AgentRadius,
            NullLogger.Instance);
        return (locomotion, CrowdOf(locomotion));
    }

    /// <summary>
    /// The crowd is an implementation detail with no accessor, but "did an agent actually reach
    /// DtCrowd" is the only honest way to test registration — a count of the class's own dictionary
    /// would pass even if AddAgent were never called.
    /// </summary>
    private static DtCrowd CrowdOf(CrowdLocomotion locomotion) =>
        (DtCrowd)typeof(CrowdLocomotion)
            .GetField("_crowd", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(locomotion)!;

    /// <summary>
    /// The dictionary <see cref="CrowdLocomotion.Update" />'s position/velocity copy-back actually
    /// enumerates. Read by reflection for the same reason as <see cref="CrowdOf" />: asserting
    /// against this dictionary, rather than against behaviour that happens to look the same, is what
    /// makes <see cref="Keep_A_Synced_Player_Out_Of_The_Creature_Copy_Back_Dictionary" /> fail if a
    /// future edit ever merges the two agent dictionaries.
    /// </summary>
    private static Dictionary<ObjectGuid, DtCrowdAgent> CreatureAgentsOf(CrowdLocomotion locomotion) =>
        (Dictionary<ObjectGuid, DtCrowdAgent>)typeof(CrowdLocomotion)
            .GetField("_creatureAgents", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(locomotion)!;

    private static Dictionary<ObjectGuid, DtCrowdAgent> PlayerAgentsOf(CrowdLocomotion locomotion) =>
        (Dictionary<ObjectGuid, DtCrowdAgent>)typeof(CrowdLocomotion)
            .GetField("_playerAgents", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(locomotion)!;

    private static ICreature CreatureAt(Vector3 position)
    {
        var creature = Substitute.For<ICreature>();
        creature.Guid.Returns(new ObjectGuid(ObjectType.Creature, 1));
        creature.Position.Returns(position);
        creature.Speed.Returns(NavmeshBuildSettings.AgentMaxSpeed);
        return creature;
    }

    /// <summary>A player's ObjectGuid is a Character guid, distinct from every CreatureAt guid above.</summary>
    private static readonly ObjectGuid PlayerGuid = new(ObjectType.Character, 500);

    [Fact]
    public void Add_An_Agent_When_A_Creature_Registers()
    {
        (CrowdLocomotion locomotion, DtCrowd crowd) = BuildOverAFlatNavMesh();
        ICreature creature = CreatureAt(Vector3.zero);

        locomotion.Register(creature, radius: 0.5f, maxSpeed: 4f);

        Assert.Single(crowd.GetActiveAgents());
    }

    /// <summary>Registering twice must not double up: MapInstance.AddCreature can be re-entered.</summary>
    [Fact]
    public void Add_Only_One_Agent_When_A_Creature_Registers_Twice()
    {
        (CrowdLocomotion locomotion, DtCrowd crowd) = BuildOverAFlatNavMesh();
        ICreature creature = CreatureAt(Vector3.zero);

        locomotion.Register(creature, radius: 0.5f, maxSpeed: 4f);
        locomotion.Register(creature, radius: 0.5f, maxSpeed: 4f);

        Assert.Single(crowd.GetActiveAgents());
    }

    /// <summary>
    /// The counterpart to WaypointLocomotionShould's equivalent test: unlike WaypointLocomotion
    /// (before its own fix), CrowdLocomotion's Register already no-ops on an already-registered
    /// creature, leaving whatever move request is in progress untouched rather than removing and
    /// re-adding the agent (which would also discard the pending move request — see Teleport's
    /// remarks on why remove/re-add clears it). Re-entrant registration must not turn a chasing
    /// creature into a stuck one.
    /// </summary>
    [Fact]
    public void Keep_An_In_Progress_Move_Request_When_Registered_Again()
    {
        (CrowdLocomotion locomotion, DtCrowd crowd) = BuildOverAFlatNavMesh();
        ICreature creature = CreatureAt(Vector3.zero);

        locomotion.Register(creature, radius: 0.5f, maxSpeed: 4f);
        locomotion.MoveTo(creature, new Vector3(15f, 0f, 15f));
        Assert.False(locomotion.HasArrived(creature));

        // Re-entrant Register while the move request above is still pending.
        locomotion.Register(creature, radius: 0.5f, maxSpeed: 4f);

        Assert.Single(crowd.GetActiveAgents());
        Assert.False(locomotion.HasArrived(creature));
    }

    [Fact]
    public void Remove_The_Agent_When_A_Creature_Unregisters()
    {
        (CrowdLocomotion locomotion, DtCrowd crowd) = BuildOverAFlatNavMesh();
        ICreature creature = CreatureAt(Vector3.zero);

        locomotion.Register(creature, radius: 0.5f, maxSpeed: 4f);
        locomotion.Unregister(creature);

        Assert.Empty(crowd.GetActiveAgents());
    }

    /// <summary>Review Focus 3, crowd side: a leaked agent would keep steering a dead creature.</summary>
    [Fact]
    public void Tolerate_Unregistering_Twice()
    {
        (CrowdLocomotion locomotion, DtCrowd crowd) = BuildOverAFlatNavMesh();
        ICreature creature = CreatureAt(Vector3.zero);

        locomotion.Register(creature, radius: 0.5f, maxSpeed: 4f);
        locomotion.Unregister(creature);
        locomotion.Unregister(creature);

        Assert.Empty(crowd.GetActiveAgents());
    }

    /// <summary>An unregistered creature is inert rather than an exception.</summary>
    [Fact]
    public void Treat_An_Unregistered_Creature_As_Already_Arrived()
    {
        (CrowdLocomotion locomotion, _) = BuildOverAFlatNavMesh();
        ICreature creature = CreatureAt(Vector3.zero);

        locomotion.MoveTo(creature, new Vector3(3f, 0f, 0f));
        locomotion.Stop(creature);
        locomotion.Update(TimeSpan.FromSeconds(1));

        Assert.True(locomotion.HasArrived(creature));
    }

    [Fact]
    public void Write_The_Agents_Position_Back_Onto_The_Creature_On_Update()
    {
        (CrowdLocomotion locomotion, _) = BuildOverAFlatNavMesh();
        ICreature creature = CreatureAt(Vector3.zero);

        locomotion.Register(creature, radius: 0.5f, maxSpeed: 4f);
        locomotion.Update(TimeSpan.FromSeconds(0.1));

        creature.Received().Position = Arg.Any<Vector3>();
    }

    [Fact]
    public void Report_Not_Arrived_While_A_Destination_Is_Pending()
    {
        (CrowdLocomotion locomotion, _) = BuildOverAFlatNavMesh();
        ICreature creature = CreatureAt(Vector3.zero);

        locomotion.Register(creature, radius: 0.5f, maxSpeed: 4f);
        locomotion.MoveTo(creature, new Vector3(15f, 0f, 15f));

        Assert.False(locomotion.HasArrived(creature));
    }

    /// <summary>
    /// The locomotion owns only the at-rest transition; the caller (a script) owns the moving
    /// MoveState and must not have it overwritten every tick.
    /// </summary>
    [Fact]
    public void Leave_The_Moving_MoveState_To_The_Caller()
    {
        (CrowdLocomotion locomotion, _) = BuildOverAFlatNavMesh();
        ICreature creature = CreatureAt(Vector3.zero);

        locomotion.Register(creature, radius: 0.5f, maxSpeed: 4f);
        locomotion.MoveTo(creature, new Vector3(15f, 0f, 15f));
        for (int i = 0; i < 5; i++)
            locomotion.Update(TimeSpan.FromSeconds(1d / 60d));

        Assert.False(locomotion.HasArrived(creature));
        creature.DidNotReceiveWithAnyArgs().MoveState = default;
    }

    /// <summary>
    /// The tolerance this class advertises has to actually describe its own arrival decision — the
    /// same value <see cref="Arrived" /> in the production class uses internally, not a
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

        locomotion.Register(creature, radius: NavmeshBuildSettings.AgentRadius, maxSpeed: 4f);
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
    /// DotRecast never clears an agent's move request on arrival, so without an explicit arrival
    /// check HasArrived would stay false forever and every chasing script would hang.
    /// </summary>
    [Fact]
    public void Come_To_Rest_Once_It_Reaches_Its_Destination()
    {
        (CrowdLocomotion locomotion, DtCrowd crowd) = BuildOverAFlatNavMesh();
        ICreature creature = CreatureAt(Vector3.zero);
        var destination = new Vector3(3f, 0f, 0f);

        locomotion.Register(creature, radius: 0.5f, maxSpeed: 4f);
        locomotion.MoveTo(creature, destination);

        // 3m at up to 4 m/s: well under ten simulated seconds even allowing for acceleration.
        for (int i = 0; i < 600 && !locomotion.HasArrived(creature); i++)
            locomotion.Update(TimeSpan.FromSeconds(1d / 60d));

        Assert.True(locomotion.HasArrived(creature));
        creature.Received().MoveState = MoveState.Idle;
        creature.Received().Velocity = Vector3.zero;

        // It arrived by walking there, not by the request failing: "arrived" and "could not path"
        // both report true, so the distance is what tells them apart.
        DtCrowdAgent agent = Assert.Single(crowd.GetActiveAgents());
        Assert.True(MathF.Abs(agent.npos.X - destination.x) < 1f, $"agent X was {agent.npos.X}");
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

        locomotion.Register(creature, radius: 0.5f, maxSpeed: 4f);
        locomotion.MoveTo(creature, new Vector3(5000f, 0f, 5000f));

        creature.Received().MoveState = MoveState.Idle;
        creature.Received().Velocity = Vector3.zero;
        Assert.True(locomotion.HasArrived(creature));
    }

    [Fact]
    public void Come_To_Rest_When_Stopped()
    {
        (CrowdLocomotion locomotion, _) = BuildOverAFlatNavMesh();
        ICreature creature = CreatureAt(Vector3.zero);

        locomotion.Register(creature, radius: 0.5f, maxSpeed: 4f);
        locomotion.MoveTo(creature, new Vector3(15f, 0f, 15f));
        locomotion.Stop(creature);

        creature.Received().MoveState = MoveState.Idle;
        creature.Received().Velocity = Vector3.zero;
        Assert.True(locomotion.HasArrived(creature));
    }

    /// <summary>
    /// A teleport that moved only the entity would leave the agent behind, and the next Update would
    /// copy the agent's old position straight back over the teleport.
    /// </summary>
    [Fact]
    public void Move_Both_The_Entity_And_The_Agent_When_Teleported()
    {
        (CrowdLocomotion locomotion, DtCrowd crowd) = BuildOverAFlatNavMesh();
        ICreature creature = CreatureAt(Vector3.zero);
        var destination = new Vector3(12f, 0f, -8f);

        locomotion.Register(creature, radius: 0.5f, maxSpeed: 4f);
        locomotion.Teleport(creature, destination);

        creature.Received().Position = destination;
        creature.Received().Velocity = Vector3.zero;
        Assert.True(locomotion.HasArrived(creature));

        DtCrowdAgent agent = Assert.Single(crowd.GetActiveAgents());
        Assert.True(MathF.Abs(agent.npos.X - destination.x) < 1f, $"agent X was {agent.npos.X}");
        Assert.True(MathF.Abs(agent.npos.Z - destination.z) < 1f, $"agent Z was {agent.npos.Z}");
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

        locomotion.Register(creature, radius: 0.5f, maxSpeed: 4f);
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

    [Fact]
    public void Register_A_Player_Agent_That_Cannot_Move_Itself()
    {
        (CrowdLocomotion locomotion, DtCrowd crowd) = BuildOverAFlatNavMesh();

        locomotion.SyncPlayer(PlayerGuid, new Vector3(1f, 0f, 1f));

        DtCrowdAgent agent = Assert.Single(crowd.GetActiveAgents());
        Assert.Equal(0f, agent.option.maxSpeed);
        Assert.Equal(0f, agent.option.maxAcceleration);
    }

    /// <summary>
    /// The server already decided where the player is. The crowd is told, never asked — so whatever
    /// Integrate and HandleCollisions compute for that agent is discarded.
    /// </summary>
    [Fact]
    public void Overwrite_A_Player_Agents_Position_Rather_Than_Reading_It_Back()
    {
        (CrowdLocomotion locomotion, DtCrowd crowd) = BuildOverAFlatNavMesh();
        locomotion.SyncPlayer(PlayerGuid, new Vector3(1f, 0f, 1f));

        locomotion.Update(TimeSpan.FromSeconds(0.1));
        locomotion.SyncPlayer(PlayerGuid, new Vector3(5f, 0f, 5f));

        DtCrowdAgent agent = Assert.Single(crowd.GetActiveAgents());
        Assert.InRange(agent.npos.X, 4.9f, 5.1f);
    }

    [Fact]
    public void Drop_A_Player_Agent_When_The_Player_Leaves()
    {
        (CrowdLocomotion locomotion, DtCrowd crowd) = BuildOverAFlatNavMesh();
        locomotion.SyncPlayer(PlayerGuid, new Vector3(1f, 0f, 1f));

        locomotion.RemovePlayer(PlayerGuid);

        Assert.Empty(crowd.GetActiveAgents());
    }

    /// <summary>Review Focus, player side: disconnects race the per-tick sync, so a second removal must not throw.</summary>
    [Fact]
    public void Tolerate_Removing_A_Player_Twice()
    {
        (CrowdLocomotion locomotion, DtCrowd crowd) = BuildOverAFlatNavMesh();
        locomotion.SyncPlayer(PlayerGuid, new Vector3(1f, 0f, 1f));

        locomotion.RemovePlayer(PlayerGuid);
        locomotion.RemovePlayer(PlayerGuid);

        Assert.Empty(crowd.GetActiveAgents());
    }

    /// <summary>Removing a player that was never synced (flag off, or never connected) is inert, not an exception.</summary>
    [Fact]
    public void Tolerate_Removing_A_Player_That_Was_Never_Synced()
    {
        (CrowdLocomotion locomotion, DtCrowd crowd) = BuildOverAFlatNavMesh();

        locomotion.RemovePlayer(PlayerGuid);

        Assert.Empty(crowd.GetActiveAgents());
    }

    /// <summary>
    /// The test that matters most for Task 9. <see cref="CrowdLocomotion.Update" />'s position and
    /// velocity copy-back enumerates <c>_creatureAgents</c> only — never <c>_playerAgents</c> — which
    /// is the entire mechanism keeping this class from fighting PlayerInputHandler for control of a
    /// player and causing rubber-banding. Asserted directly against the two dictionaries rather than
    /// indirectly through behaviour, because there is no ICreature for a player to observe a write on
    /// in the first place — SyncPlayer takes a bare Vector3, so the absence of an observable write is
    /// not, by itself, proof that the copy-back excludes players. This fails if a future edit ever
    /// merges the two dictionaries, or if SyncPlayer is changed to add into <c>_creatureAgents</c>.
    /// </summary>
    [Fact]
    public void Keep_A_Synced_Player_Out_Of_The_Creature_Copy_Back_Dictionary()
    {
        (CrowdLocomotion locomotion, _) = BuildOverAFlatNavMesh();
        ICreature creature = CreatureAt(Vector3.zero);
        locomotion.Register(creature, radius: 0.5f, maxSpeed: 4f);

        locomotion.SyncPlayer(PlayerGuid, new Vector3(1f, 0f, 1f));
        locomotion.Update(TimeSpan.FromSeconds(0.1));

        Assert.Contains(PlayerGuid, PlayerAgentsOf(locomotion).Keys);
        Assert.DoesNotContain(PlayerGuid, CreatureAgentsOf(locomotion).Keys);

        // Belt and braces: the only ICreature this class knows about at all is the registered
        // creature above, so nothing about the player could have been written even if the
        // dictionaries above had been merged. This pins that no exception was thrown reaching the
        // player guid inside the copy-back loop, i.e. Update tolerates an agent dictionary Update
        // itself never reads from.
        creature.DidNotReceive().Position = new Vector3(1f, 0f, 1f);
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

        locomotion.Register(creature, radius: 0.5f, maxSpeed: 4f);
        locomotion.SyncPlayer(PlayerGuid, playerPosition);
        locomotion.MoveTo(creature, destination);

        DtCrowdAgent creatureAgent = CreatureAgentsOf(locomotion)[creature.Guid];

        // Re-sync the player every tick, exactly as MapInstance.Update does — a stationary player
        // whose position is never re-pushed would still be "stationary" for this test, but the real
        // per-tick contract is what production code actually runs.
        float maxLateralDeviation = 0f;
        for (int i = 0; i < 300 && creatureAgent.npos.X < 0f; i++)
        {
            locomotion.SyncPlayer(PlayerGuid, playerPosition);
            locomotion.Update(TimeSpan.FromSeconds(1d / 60d));
            maxLateralDeviation = MathF.Max(maxLateralDeviation, MathF.Abs(creatureAgent.npos.Z));
        }

        Assert.True(maxLateralDeviation > 0.2f,
            $"creature passed the player's X position with only {maxLateralDeviation}m of lateral " +
            "deviation from the straight line it started on");
    }
}
