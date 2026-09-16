using Avalon.Common.ValueObjects;
using Avalon.World;
using Avalon.World.Characters;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Instances;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Characters;

/// <summary>
/// A character is built by select and spawned by this. Between the two it is invisible to the tick,
/// so the two ways out of that state -- the client's load report and the expiring barrier -- are the
/// only things standing between a connected player and never entering the world at all.
/// </summary>
public class CharacterReadinessBarrierShould
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private static (IWorldConnection connection, ICharacter character, IMapInstance instance)
        Pending(long sinceTicks)
    {
        ICharacter character = PendingSpawnConnection.Character();
        var instance = Substitute.For<IMapInstance>();

        IWorldConnection connection =
            PendingSpawnConnection.Create(new PendingSpawn(character, instance, sinceTicks));

        return (connection, character, instance);
    }

    [Fact]
    public void Not_spawn_a_character_whose_barrier_has_not_expired()
    {
        long now = DateTime.UtcNow.Ticks;
        var (connection, _, _) = Pending(now - TimeSpan.FromSeconds(14).Ticks);
        IWorld world = Substitute.For<IWorld>();

        CharacterReadinessBarrier.ReleaseExpired([connection], world, now, Timeout,
            NullLogger.Instance);

        world.DidNotReceiveWithAnyArgs().SpawnInInstance(default!, default!);
        Assert.Null(connection.Character);
        Assert.NotNull(connection.PendingSpawn);
    }

    [Fact]
    public void Spawn_a_character_whose_barrier_has_expired()
    {
        long now = DateTime.UtcNow.Ticks;
        var (connection, character, instance) = Pending(now - TimeSpan.FromSeconds(16).Ticks);
        IWorld world = Substitute.For<IWorld>();

        CharacterReadinessBarrier.ReleaseExpired([connection], world, now, Timeout,
            NullLogger.Instance);

        world.Received(1).SpawnInInstance(connection, instance);
        Assert.Same(character, connection.Character);
        Assert.Null(connection.PendingSpawn);
    }

    /// <summary>
    /// The sweep runs every tick. Taking the pending spawn is what stops a released character
    /// being spawned again sixty times a second for as long as it stays connected.
    /// </summary>
    [Fact]
    public void Spawn_an_expired_character_once_however_many_ticks_pass()
    {
        long now = DateTime.UtcNow.Ticks;
        var (connection, _, _) = Pending(now - TimeSpan.FromSeconds(16).Ticks);
        IWorld world = Substitute.For<IWorld>();

        CharacterReadinessBarrier.ReleaseExpired([connection], world, now, Timeout, NullLogger.Instance);
        CharacterReadinessBarrier.ReleaseExpired([connection], world, now, Timeout, NullLogger.Instance);
        CharacterReadinessBarrier.ReleaseExpired([connection], world, now, Timeout, NullLogger.Instance);

        world.ReceivedWithAnyArgs(1).SpawnInInstance(default!, default!);
    }

    [Fact]
    public void Spawn_on_the_clients_report_without_waiting_for_the_barrier()
    {
        var (connection, character, instance) = Pending(DateTime.UtcNow.Ticks);
        IWorld world = Substitute.For<IWorld>();

        Assert.True(CharacterReadinessBarrier.Release(connection, world, NullLogger.Instance));

        world.Received(1).SpawnInInstance(connection, instance);
        Assert.Same(character, connection.Character);
    }

    [Fact]
    public void Spawn_nothing_when_no_character_is_pending()
    {
        IWorldConnection connection = PendingSpawnConnection.Create();
        IWorld world = Substitute.For<IWorld>();

        Assert.False(CharacterReadinessBarrier.Release(connection, world, NullLogger.Instance));

        world.DidNotReceiveWithAnyArgs().SpawnInInstance(default!, default!);
        Assert.Null(connection.Character);
    }

    /// <summary>
    /// A half-released character -- assigned but not in the instance -- would be visible to every
    /// reader of Character while belonging to no map.
    /// </summary>
    [Fact]
    public void Leave_the_connection_without_a_character_when_the_spawn_throws()
    {
        var (connection, _, _) = Pending(DateTime.UtcNow.Ticks);
        IWorld world = Substitute.For<IWorld>();
        world.When(w => w.SpawnInInstance(Arg.Any<IWorldConnection>(), Arg.Any<IMapInstance>()))
            .Do(_ => throw new InvalidOperationException("instance is full"));

        Assert.False(CharacterReadinessBarrier.Release(connection, world, NullLogger.Instance));

        Assert.Null(connection.Character);
    }

    /// <summary>
    /// The despawn writes the row back through the pending spawn and nothing else. Releasing takes
    /// it, so a spawn that threw would otherwise leave the character reachable from nowhere and the
    /// row online for good.
    /// </summary>
    [Fact]
    public void Hand_the_pending_spawn_back_and_close_when_the_spawn_throws()
    {
        var (connection, character, instance) = Pending(DateTime.UtcNow.Ticks);
        IWorld world = Substitute.For<IWorld>();
        world.When(w => w.SpawnInInstance(Arg.Any<IWorldConnection>(), Arg.Any<IMapInstance>()))
            .Do(_ => throw new InvalidOperationException("instance is full"));

        CharacterReadinessBarrier.Release(connection, world, NullLogger.Instance);

        Assert.NotNull(connection.PendingSpawn);
        Assert.Same(character, connection.PendingSpawn.Character);
        Assert.Same(instance, connection.PendingSpawn.Instance);
        connection.Received().Close(Arg.Any<bool>());
    }

    /// <summary>
    /// The sweep runs sixty times a second over every connection. A dropped one belongs to the
    /// despawn, and skipping it is also what stops a spawn that threw being retried forever.
    /// </summary>
    [Fact]
    public void Leave_a_dropped_connection_to_the_despawn()
    {
        long now = DateTime.UtcNow.Ticks;
        var (connection, _, _) = Pending(now - TimeSpan.FromSeconds(16).Ticks);
        connection.IsConnected.Returns(false);
        IWorld world = Substitute.For<IWorld>();

        CharacterReadinessBarrier.ReleaseExpired([connection], world, now, Timeout, NullLogger.Instance);

        world.DidNotReceiveWithAnyArgs().SpawnInInstance(default!, default!);
        Assert.NotNull(connection.PendingSpawn);
    }
}
