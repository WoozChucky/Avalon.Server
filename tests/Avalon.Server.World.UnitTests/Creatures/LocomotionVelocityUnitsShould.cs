using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Network.Packets.State;
using Avalon.World.Creatures.Locomotion;
using Avalon.World.Maps.Navigation;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Maps;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Creatures;

/// <summary>
/// Issue #424. Both locomotions publish <see cref="Avalon.World.Public.IWorldObject.Velocity" /> in
/// metres per second, because that is what the client multiplies by elapsed seconds to extrapolate
/// a creature between state broadcasts. WaypointLocomotion used to publish a unit direction, so
/// every creature it moved was extrapolated at 1 m/s whatever its speed, and switching the
/// configured locomotion changed the magnitude on the wire for every creature.
/// </summary>
public class LocomotionVelocityUnitsShould
{
    private const float Tick = 1f / 60f;

    /// <summary>Loose enough for the crowd's steering, tight enough that a unit vector (1 m/s) fails.</summary>
    private const float Tolerance = 0.15f;

    private static readonly Vector3 Start = new(-10f, 0f, 0f);
    private static readonly Vector3 Destination = new(10f, 0f, 0f);

    public static TheoryData<string, float> Cases() => new()
    {
        { "waypoint", 2f },
        { "waypoint", NavmeshBuildSettings.AgentMaxSpeed },
        { "crowd", 2f },
        { "crowd", NavmeshBuildSettings.AgentMaxSpeed },
    };

    /// <summary>
    /// The creature's Position and Velocity are left as plain substitute properties, so whatever
    /// the locomotion writes is what the test reads back.
    /// </summary>
    private static ICreature CreatureAt(Vector3 position, float speed)
    {
        var creature = Substitute.For<ICreature>();
        creature.Guid.Returns(new ObjectGuid(ObjectType.Creature, 1));
        creature.Speed.Returns(speed);
        creature.Position = position;
        return creature;
    }

    private static ICreatureLocomotion Build(string kind)
    {
        if (kind == "crowd")
        {
            return new CrowdLocomotion(CrowdLocomotionShould.FlatNavMesh.Value, NavmeshBuildSettings.AgentRadius,
                NullLogger.Instance);
        }

        var navigator = Substitute.For<IMapNavigator>();
        navigator.FindPath(Arg.Any<Vector3>(), Arg.Any<Vector3>()).Returns([Destination]);
        return new WaypointLocomotion(_ => navigator);
    }

    private static void Tick_(ICreatureLocomotion locomotion, int ticks)
    {
        for (int i = 0; i < ticks; i++)
            locomotion.Update(TimeSpan.FromSeconds(Tick));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Publish_Velocity_In_Metres_Per_Second_While_Moving(string kind, float speed)
    {
        ICreatureLocomotion locomotion = Build(kind);
        ICreature creature = CreatureAt(Start, speed);

        locomotion.Register(creature, radius: NavmeshBuildSettings.AgentRadius);
        locomotion.MoveTo(creature, Destination);

        // One second: past the crowd's acceleration ramp (3.5 m/s at 8 m/s^2 takes under half a
        // second) and well short of the 20 m journey at either speed.
        Tick_(locomotion, 60);

        Assert.False(locomotion.HasArrived(creature));
        Vector3 velocity = creature.Velocity;
        Assert.InRange(velocity.magnitude, speed - Tolerance, speed + Tolerance);
        Assert.True(velocity.x > 0f, $"{kind} velocity should point at the destination, was {velocity}");
    }

    /// <summary>
    /// Dead reckoning is only as good as the agreement between Velocity and how far Position
    /// actually moves: over one tick the creature should travel Velocity * deltaTime.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void Move_Position_By_Velocity_Times_Elapsed_Seconds(string kind, float speed)
    {
        ICreatureLocomotion locomotion = Build(kind);
        ICreature creature = CreatureAt(Start, speed);

        locomotion.Register(creature, radius: NavmeshBuildSettings.AgentRadius);
        locomotion.MoveTo(creature, Destination);
        Tick_(locomotion, 60);

        Vector3 before = creature.Position;
        locomotion.Update(TimeSpan.FromSeconds(Tick));
        float travelled = (creature.Position - before).magnitude;
        float expected = creature.Velocity.magnitude * Tick;

        Assert.InRange(travelled, expected * 0.9f, expected * 1.1f);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Publish_Zero_Velocity_Once_At_Rest(string kind, float speed)
    {
        ICreatureLocomotion locomotion = Build(kind);
        ICreature creature = CreatureAt(Start, speed);

        locomotion.Register(creature, radius: NavmeshBuildSettings.AgentRadius);
        locomotion.MoveTo(creature, Destination);

        // 20 m at 2 m/s is ten seconds; allow twenty.
        for (int i = 0; i < 1200 && !locomotion.HasArrived(creature); i++)
            locomotion.Update(TimeSpan.FromSeconds(Tick));

        Assert.True(locomotion.HasArrived(creature));
        Assert.Equal(Vector3.zero, creature.Velocity);
        Assert.Equal(MoveState.Idle, creature.MoveState);

        // And it stays zero on the ticks after, with nothing left to walk.
        Tick_(locomotion, 1);
        Assert.Equal(Vector3.zero, creature.Velocity);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Publish_Zero_Velocity_When_Stopped_Mid_Journey(string kind, float speed)
    {
        ICreatureLocomotion locomotion = Build(kind);
        ICreature creature = CreatureAt(Start, speed);

        locomotion.Register(creature, radius: NavmeshBuildSettings.AgentRadius);
        locomotion.MoveTo(creature, Destination);
        Tick_(locomotion, 30);

        locomotion.Stop(creature);
        Assert.Equal(Vector3.zero, creature.Velocity);

        Tick_(locomotion, 1);
        Assert.Equal(Vector3.zero, creature.Velocity);
    }
}
