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

        // Locomotion owns only the at-rest transition; the caller (a script) owns the moving
        // MoveState (Walking vs Running) and must not have it overwritten every tick.
        creature.DidNotReceiveWithAnyArgs().MoveState = default;
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

    /// <summary>
    /// The tolerance this class advertises has to actually describe its own arrival decision, or a
    /// caller relying on it (CreatureCombatScript's in-range check) would under- or over-trust how
    /// close "arrived" really means. Stands 0.05m short of the waypoint — inside the 0.1f arrival
    /// epsilon so the waypoint is consumed and HasArrived flips true, but not exactly on it, so the
    /// check is not trivially satisfied by both sides being zero.
    /// </summary>
    [Fact]
    public void Report_A_Tolerance_Consistent_With_Its_Own_Arrival_Decision()
    {
        var (locomotion, navigator) = Build();
        ICreature creature = CreatureAt(Vector3.zero);
        var destination = new Vector3(1f, 0f, 0f);
        navigator.FindPath(Arg.Any<Vector3>(), Arg.Any<Vector3>()).Returns([destination]);
        creature.Position.Returns(new Vector3(0.95f, 0f, 0f));

        locomotion.Register(creature, radius: 0.5f, maxSpeed: 4f);
        locomotion.MoveTo(creature, destination);
        locomotion.Update(TimeSpan.FromSeconds(0.1)); // consumes the waypoint -> HasArrived == true

        Assert.True(locomotion.HasArrived(creature));
        float distanceFromDestination = Vector3.Distance(creature.Position, destination);
        Assert.True(distanceFromDestination <= locomotion.ArrivalTolerance(creature),
            $"reported arrived {distanceFromDestination}m from destination but advertises only " +
            $"{locomotion.ArrivalTolerance(creature)}m tolerance");
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

    /// <summary>
    /// A leash-return teleport must discard whatever path was queued: otherwise the next Update
    /// would walk the creature straight back along the stale route from before it was teleported.
    /// </summary>
    [Fact]
    public void Discard_A_Queued_Path_When_Teleported()
    {
        var (locomotion, navigator) = Build();
        ICreature creature = CreatureAt(Vector3.zero);
        navigator.FindPath(Arg.Any<Vector3>(), Arg.Any<Vector3>())
            .Returns([new Vector3(10f, 0f, 0f)]);

        locomotion.Register(creature, radius: 0.5f, maxSpeed: 4f);
        locomotion.MoveTo(creature, new Vector3(10f, 0f, 0f));
        locomotion.Teleport(creature, new Vector3(50f, 0f, 50f));

        creature.Position.Returns(new Vector3(50f, 0f, 50f));
        locomotion.Update(TimeSpan.FromSeconds(1));

        // The stale path towards (10, 0, 0) must not move the creature after the teleport.
        creature.DidNotReceive().Position = Arg.Is<Vector3>(p => p != new Vector3(50f, 0f, 50f));
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

    /// <summary>
    /// Regression: Register used to unconditionally replace the Agent with a fresh, empty one —
    /// MapInstance.AddCreature can be re-entered for a creature that is already mid-path, and a
    /// reset there cleared the queue without ever calling ComeToRest. That left Velocity and
    /// MoveState at whatever moving value the caller last set while Position never changed again
    /// — the client extrapolates a creature stuck running on the spot forever. CrowdLocomotion
    /// already no-ops on a second Register for an already-registered creature; this pins
    /// WaypointLocomotion matching that instead of resetting.
    /// </summary>
    [Fact]
    public void Keep_Advancing_An_Already_Moving_Creature_When_Registered_Again()
    {
        var (locomotion, navigator) = Build();
        ICreature creature = CreatureAt(Vector3.zero);
        navigator.FindPath(Arg.Any<Vector3>(), Arg.Any<Vector3>())
            .Returns([new Vector3(10f, 0f, 0f)]);

        locomotion.Register(creature, radius: 0.5f, maxSpeed: 4f);
        locomotion.MoveTo(creature, new Vector3(10f, 0f, 0f));
        Assert.False(locomotion.HasArrived(creature));

        // Re-entrant Register while the path above is still loaded — must be a no-op.
        locomotion.Register(creature, radius: 0.5f, maxSpeed: 4f);
        Assert.False(locomotion.HasArrived(creature));

        locomotion.Update(TimeSpan.FromSeconds(1));

        // Still walking the original path toward (10, 0, 0) — 4 m/s for one second along +X —
        // not stuck at the origin with a cleared queue.
        creature.Received().Position = Arg.Is<Vector3>(p => p.x > 3.9f && p.x < 4.1f);
        creature.DidNotReceive().MoveState = MoveState.Idle;
    }
}
