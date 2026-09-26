using System.Diagnostics;
using Avalon.Server.World.UnitTests.Characters;
using Avalon.Network.Packets.Character;
using Avalon.World;
using Avalon.World.Handlers;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Instances;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Handlers;

/// <summary>
/// CMSG_CHARACTER_LOADED is the packet that puts a player in the world. Before it existed the
/// select handler spawned the character itself, so other players saw a character whose client was
/// still assembling a map.
/// </summary>
public class CharacterLoadedHandlerShould
{
    private static (CharacterLoadedHandler handler, IWorldConnection connection, IWorld world,
        ICharacter character, IMapInstance instance) Build(bool pending)
    {
        ICharacter character = PendingSpawnConnection.Character();
        var instance = Substitute.For<IMapInstance>();

        IWorldConnection connection = PendingSpawnConnection.Create(
            pending ? new PendingSpawn(character, instance, DateTime.UtcNow.Ticks) : null);

        IWorld world = Substitute.For<IWorld>();
        var handler = new CharacterLoadedHandler(NullLogger<CharacterLoadedHandler>.Instance, world);
        return (handler, connection, world, character, instance);
    }

    /// <summary>
    /// The dispatcher's "packet CMSG_CHARACTER_LOADED" span is the server entry point; the handler's
    /// own span nests under it and is Internal, or the service graph counts the packet twice.
    /// </summary>
    [Fact]
    public void Trace_itself_as_an_internal_span_under_the_packet_span()
    {
        var (handler, connection, _, _, _) = Build(pending: true);
        using ActivitySource dispatcher = new($"test-{Guid.NewGuid()}");
        List<Activity> stopped = [];
        using ActivityListener listener = new()
        {
            ShouldListenTo = s => s.Name == "world-server" || s == dispatcher,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => { lock (stopped) stopped.Add(a); },
        };
        ActivitySource.AddActivityListener(listener);

        Activity packet = dispatcher.StartActivity("packet CMSG_CHARACTER_LOADED", ActivityKind.Server)!;
        handler.Execute(connection, new CCharacterLoadedPacket());
        packet.Dispose();

        Activity inner;
        lock (stopped)
            inner = Assert.Single(stopped, a => a.ParentSpanId == packet.SpanId);
        Assert.Equal(nameof(CharacterLoadedHandler), inner.DisplayName);
        Assert.Equal(ActivityKind.Internal, inner.Kind);
    }

    [Fact]
    public void Spawn_the_pending_character()
    {
        var (handler, connection, world, character, instance) = Build(pending: true);

        handler.Execute(connection, new CCharacterLoadedPacket());

        world.Received(1).SpawnInInstance(connection, instance);
        Assert.Same(character, connection.Character);
        Assert.Null(connection.PendingSpawn);
    }

    /// <summary>
    /// A report that arrives after the barrier already spawned the character, or twice, or from a
    /// client that never selected. None of those is worth dropping a connected player for.
    /// </summary>
    [Fact]
    public void Do_nothing_and_stay_connected_when_no_spawn_is_pending()
    {
        var (handler, connection, world, _, _) = Build(pending: false);

        handler.Execute(connection, new CCharacterLoadedPacket());

        world.DidNotReceiveWithAnyArgs().SpawnInInstance(default!, default!);
        connection.DidNotReceiveWithAnyArgs().Close(default);
        Assert.Null(connection.Character);
    }
}
