using System.Diagnostics.Metrics;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.Telemetry;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.World;
using Avalon.Server.World.UnitTests.GameAuth;
using Avalon.Server.World.UnitTests.Parties;
using Avalon.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Maps;
using Avalon.World.Persistence;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Respawn;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>
/// #969: a build that never finishes must not strand the characters waiting on it. Every move that waits on a build,
/// through the real registry on the world's clock: a portal entry (into the character's forest), a return to town
/// (<see cref="TownReturn" />, which a respawn, a town scroll and a party's leave countdown start) and an item's teleport
/// (into the forest).
/// </summary>
public class InstanceBuildDeadlineShould
{
    public enum Move
    {
        PortalEntry,
        ReturnToTown,
        Teleport,
    }

    private const ushort TownMap = 1;
    private const ushort ForestMap = 2;
    private static readonly TimeSpan s_bound = TimeSpan.FromSeconds(5);

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
    private readonly List<TaskCompletionSource<MapInstance>> _builds = [];
    private readonly List<(Task Task, Action Run)> _queued = [];
    private readonly PartyTestWorld _parties = new();
    private readonly IWorld _world = Substitute.For<IWorld>();
    private readonly InstanceRegistry _registry;
    private readonly MapInstance _source;

    public InstanceBuildDeadlineShould()
    {
        // MaxPlayers 0 counts every town full, so a return to town asks for a build, as a crowded town does.
        List<MapTemplate> templates =
        [
            new() { Id = new MapTemplateId(TownMap), MapType = MapType.Town, MaxPlayers = 0, Name = "town", Description = "" },
            new() { Id = new MapTemplateId(ForestMap), MapType = MapType.Normal, Name = "forest", Description = "" },
        ];
        IAvalonMapManager maps = Substitute.For<IAvalonMapManager>();
        maps.Templates.Returns(templates);

        // Every build waits for the test: a build that never finishes is one never completed. Not asynchronous, so a
        // completed one is queued for publication at once.
        IChunkLayoutInstanceFactory factory = Substitute.For<IChunkLayoutInstanceFactory>();
        factory.BuildAsync(default!, default, default, default).ReturnsForAnyArgs(_ =>
        {
            var build = new TaskCompletionSource<MapInstance>();
            _builds.Add(build);
            return build.Task;
        });
        _registry = new InstanceRegistry(NullLoggerFactory.Instance, maps, factory, time: _clock);

        _world.InstanceRegistry.Returns(_registry);
        _world.PartyInstances.Returns(_registry);
        _world.MapTemplates.Returns(templates);
        _world.Configuration.Returns(_parties.Config);

        // The town the character stands in, on its portal to the forest, published as the tick publishes a build.
        _source = TestMapInstances.Build(MapInstanceClients.NewWorld(), mapType: MapType.Town,
            templateId: new MapTemplateId(TownMap));
        _source.AddPortal(new PortalInstance(new ObjectGuid(ObjectType.Portal, 1), Vector3.zero, 5f, ForestMap, 1));
        Task<IMapInstance> town = _registry.GetOrCreateTownInstanceAsync(new MapTemplateId(TownMap), 0);
        Complete(0, _source);
        Assert.True(town.IsCompletedSuccessfully);
    }

    /// <summary>
    /// The build behind the move never finishes. Until its 30 s are up the move is under way; at 30 s the registry
    /// abandons the build and the move ends through the path any failed build takes: the mark cleared, the player told
    /// once, the timeout counted once. The next request starts a new build and arrives; the abandoned build finishing
    /// after all is discarded, never published, and moves nobody a second time.
    /// </summary>
    [Theory]
    [InlineData(Move.PortalEntry)]
    [InlineData(Move.ReturnToTown)]
    [InlineData(Move.Teleport)]
    public async Task Answer_every_move_waiting_on_a_build_that_never_finishes_and_discard_the_build(Move move)
    {
        using var timeouts = new TimeoutProbe();
        PartyClient client = Online();

        Start(move, client);
        await Flush();
        Assert.Equal(2, _builds.Count);

        // Just short of the deadline: still waiting, still marked.
        _clock.Advance(InstanceRegistry.BuildDeadline - TimeSpan.FromMilliseconds(1));
        _registry.PublishFinished();
        await Flush();
        Assert.True(client.Connection.RespawnInFlight);
        Assert.Empty(client.Lines());

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        _registry.PublishFinished();
        await Flush(settle: true);

        Assert.False(client.Connection.RespawnInFlight);
        Assert.Equal([InstanceBuildTimeoutException.PlayerMessage], client.Lines());
        Assert.Equal(1, timeouts.Count);
        Assert.Equal(1, _registry.AbandonedBuildsRunning);
        _world.DidNotReceiveWithAnyArgs().TransferPlayer(default!, default!);

        // Asked again: a new build, not the abandoned one.
        Start(move, client);
        await Flush();
        Assert.Equal(3, _builds.Count);

        // The abandoned build finishes after all: discarded, not registered, nobody moved into it.
        MapInstance late = Instance(move);
        Complete(1, late);
        await Flush();
        Assert.Null(_registry.GetInstanceById(late.InstanceId));
        Assert.Equal(0, _registry.AbandonedBuildsRunning);
        _world.DidNotReceiveWithAnyArgs().TransferPlayer(default!, default!);

        MapInstance fresh = Instance(move);
        Complete(2, fresh);
        await Flush(settle: true);

        _world.Received(1).TransferPlayer(client.Connection, Arg.Any<IMapInstance>());
        _world.Received(1).TransferPlayer(client.Connection, fresh);
        Assert.False(client.Connection.RespawnInFlight);
        Assert.Single(client.Lines());
        Assert.Equal(1, timeouts.Count);
    }

    /// <summary>A character in the town on its portal, behind a connection that queues its continuations for <see cref="Flush" />.</summary>
    private PartyClient Online()
    {
        PartyClient client = _parties.Online(7, instance: _source.InstanceId);
        client.Character.Position = Vector3.zero;
        client.Connection.InGame.Returns(true);
        ((ISettledContinuations)client.Connection)
            .When(c => c.EnqueueSettled(Arg.Any<Task<IMapInstance>>(), Arg.Any<Action<Task<IMapInstance>>>()))
            .Do(call =>
            {
                Task<IMapInstance> task = call.Arg<Task<IMapInstance>>();
                Action<Task<IMapInstance>> callback = call.Arg<Action<Task<IMapInstance>>>();
                _queued.Add((task, () => callback(task)));
            });
        client.Connection.When(c => c.EnqueueContinuation(Arg.Any<Task>(), Arg.Any<Action>()))
            .Do(call => _queued.Add((call.Arg<Task>(), call.Arg<Action>())));
        return client;
    }

    private void Start(Move move, PartyClient client)
    {
        switch (move)
        {
            case Move.PortalEntry:
                new EnterMapHandler(NullLogger<EnterMapHandler>.Instance, Substitute.For<ICharacterSaver>(), _world,
                    _parties.Parties).Execute(client.Connection, new CEnterMapPacket { TargetMapId = ForestMap });
                break;

            case Move.ReturnToTown:
                IRespawnTargetResolver resolver = Substitute.For<IRespawnTargetResolver>();
                resolver.ResolveTownAsync(Arg.Any<MapTemplateId>(), Arg.Any<CancellationToken>())
                    .Returns(new MapTemplateId(TownMap));
                client.Connection.RespawnInFlight = true; // as each caller marks it before starting the return
                new TownReturn(NullLogger.Instance, _world, resolver, InlineDatabaseWork.Instance)
                    .Start(client.Connection, revive: false, dropEncounter: true);
                break;

            case Move.Teleport:
                Assert.True(new MapTeleport(NullLogger<MapTeleport>.Instance, _world, Substitute.For<ICharacterSaver>(),
                    _parties.Parties).Start(client.Connection, new MapTemplateId(ForestMap), position: null));
                break;
        }
    }

    /// <summary>
    /// Runs the queued continuations whose task has ended, as one tick's flush does; the others, and any they queue,
    /// wait for the next call. <paramref name="settle" /> first waits for each to end: a continuation that settles its
    /// task through one of its own runs once a thread-pool thread has run that.
    /// </summary>
    private async Task Flush(bool settle = false)
    {
        List<(Task Task, Action Run)> due = [.. _queued];
        _queued.Clear();
        foreach ((Task task, Action run) in due)
        {
            if (settle)
                await Task.WhenAny(task).WaitAsync(s_bound);

            if (task.IsCompleted)
                run();
            else
                _queued.Add((task, run));
        }
    }

    /// <summary>Completes build <paramref name="index" /> with <paramref name="instance" /> and publishes, as the tick does first.</summary>
    private void Complete(int index, MapInstance instance)
    {
        _builds[index].SetResult(instance);
        _registry.PublishFinished();
    }

    /// <summary>An instance of the map <paramref name="move" /> leads to, as its build makes it.</summary>
    private static MapInstance Instance(Move move) => move == Move.ReturnToTown
        ? TestMapInstances.Build(MapInstanceClients.NewWorld(), mapType: MapType.Town, templateId: new MapTemplateId(TownMap))
        : TestMapInstances.Build(MapInstanceClients.NewWorld(), mapType: MapType.Normal, templateId: new MapTemplateId(ForestMap));

    /// <summary>
    /// The builds counted in <c>world.instance.build_timeouts</c> on this test's own flow: the counter is static and
    /// shared, and other tests run in parallel.
    /// </summary>
    private sealed class TimeoutProbe : IDisposable
    {
        private static readonly AsyncLocal<TimeoutProbe?> s_current = new();
        private readonly MeterListener _listener = new();

        public TimeoutProbe()
        {
            s_current.Value = this;
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter == DiagnosticsConfig.World.Meter && instrument.Name == "world.instance.build_timeouts")
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((_, value, _, _) =>
            {
                if (s_current.Value == this)
                    Count += (int)value;
            });
            _listener.Start();
        }

        public int Count { get; private set; }

        public void Dispose() => _listener.Dispose();
    }
}
