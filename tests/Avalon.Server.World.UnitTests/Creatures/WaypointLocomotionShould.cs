using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Network.Packets.State;
using Avalon.World.Creatures.Locomotion;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Maps;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Creatures;

/// <summary>
/// The waypoint implementation is what the world has always done, lifted out of the two creature
/// scripts that each carried their own near-identical copy of it.
/// </summary>
public class WaypointLocomotionShould
{
    private static ICreature CreatureAt(Vector3 position)
    {
        var creature = Substitute.For<ICreature>();
        creature.Guid.Returns(new Avalon.Common.ObjectGuid(ObjectType.Creature, 1));
        creature.Position.Returns(position);
        creature.Speed.Returns(4f);
        return creature;
    }

    private static (WaypointLocomotion Locomotion, IMapNavigator Navigator) Build()
    {
        var navigator = Substitute.For<IMapNavigator>();
        return (new WaypointLocomotion(_ => navigator), navigator);
    }

    [Fact]
    public void Advance_A_Creature_Toward_Its_Destination()
    {
        var (locomotion, navigator) = Build();
        ICreature creature = CreatureAt(Vector3.zero);
        navigator.FindPath(Arg.Any<Vector3>(), Arg.Any<Vector3>())
            .Returns([new Vector3(10f, 0f, 0f)]);

        locomotion.Register(creature, radius: 0.5f, maxSpeed: 4f);
        locomotion.MoveTo(creature, new Vector3(10f, 0f, 0f));
        locomotion.Update(TimeSpan.FromSeconds(1));

        // 4 m/s for one second along +X.
        creature.Received().Position = Arg.Is<Vector3>(p => p.x > 3.9f && p.x < 4.1f);
        creature.Received().MoveState = MoveState.Walking;
    }

    /// <summary>
    /// Review Focus 2. CreatureCombatScript carries a comment about this exact drift: an empty path
    /// leaving MoveState at a moving value while Position never changes, so the client animates a
    /// creature running on the spot forever.
    /// </summary>
    [Fact]
    public void Come_To_Rest_When_No_Path_Exists()
    {
        var (locomotion, navigator) = Build();
        ICreature creature = CreatureAt(Vector3.zero);
        navigator.FindPath(Arg.Any<Vector3>(), Arg.Any<Vector3>()).Returns([]);

        locomotion.Register(creature, radius: 0.5f, maxSpeed: 4f);
        locomotion.MoveTo(creature, new Vector3(10f, 0f, 0f));
        locomotion.Update(TimeSpan.FromSeconds(1));

        creature.Received().MoveState = MoveState.Idle;
        creature.Received().Velocity = Vector3.zero;
        Assert.True(locomotion.HasArrived(creature));
    }

    [Fact]
    public void Report_Arrival_Once_The_Last_Waypoint_Is_Consumed()
    {
        var (locomotion, navigator) = Build();
        ICreature creature = CreatureAt(Vector3.zero);
        navigator.FindPath(Arg.Any<Vector3>(), Arg.Any<Vector3>())
            .Returns([new Vector3(1f, 0f, 0f)]);
        creature.Position.Returns(new Vector3(1f, 0f, 0f)); // already standing on it

        locomotion.Register(creature, radius: 0.5f, maxSpeed: 4f);
        locomotion.MoveTo(creature, new Vector3(1f, 0f, 0f));
        locomotion.Update(TimeSpan.FromSeconds(0.1));

        Assert.True(locomotion.HasArrived(creature));
    }

    [Fact]
    public void Place_A_Teleported_Creature_Without_Walking_It()
    {
        var (locomotion, _) = Build();
        ICreature creature = CreatureAt(Vector3.zero);

        locomotion.Register(creature, radius: 0.5f, maxSpeed: 4f);
        locomotion.Teleport(creature, new Vector3(50f, 0f, 50f));

        creature.Received().Position = new Vector3(50f, 0f, 50f);
        creature.Received().Velocity = Vector3.zero;
        Assert.True(locomotion.HasArrived(creature));
    }

    /// <summary>Review Focus 3. Despawn can race a script update.</summary>
    [Fact]
    public void Tolerate_Register_And_Unregister_Being_Called_Twice()
    {
        var (locomotion, _) = Build();
        ICreature creature = CreatureAt(Vector3.zero);

        locomotion.Register(creature, radius: 0.5f, maxSpeed: 4f);
        locomotion.Register(creature, radius: 0.5f, maxSpeed: 4f);
        locomotion.Unregister(creature);
        locomotion.Unregister(creature);

        // An unregistered creature is inert rather than an exception.
        locomotion.MoveTo(creature, new Vector3(1f, 0f, 0f));
        locomotion.Update(TimeSpan.FromSeconds(1));
        Assert.True(locomotion.HasArrived(creature));
    }
}
