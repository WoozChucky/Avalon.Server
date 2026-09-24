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
    /// expensive part and a <see cref="DtCrowd" /> is built per test anyway.
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

    private static ICreature CreatureAt(Vector3 position)
    {
        var creature = Substitute.For<ICreature>();
        creature.Guid.Returns(new ObjectGuid(ObjectType.Creature, 1));
        creature.Position.Returns(position);
        creature.Speed.Returns(NavmeshBuildSettings.AgentMaxSpeed);
        return creature;
    }

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
}
