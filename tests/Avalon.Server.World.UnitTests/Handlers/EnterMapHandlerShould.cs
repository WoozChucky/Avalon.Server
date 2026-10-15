using System.Diagnostics.Metrics;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.Telemetry;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Party;
using Avalon.Network.Packets.State;
using Avalon.Network.Packets.World;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.Server.World.UnitTests.Parties;
using Avalon.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Parties;
using Avalon.World.Persistence;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Maps;
using Avalon.World.Scripts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Handlers;

public class EnterMapHandlerShould
{
    private const ushort TownMap = 1;
    private const ushort TargetMap = 2;

    /// <summary>
    /// A connection kicked by a select of its character on another connection has released it
    /// before the target instance is ready. The continuation still runs, and must not dereference
    /// the character it no longer holds, nor transfer an entity that has already left.
    /// </summary>
    [Fact]
    public void Do_nothing_when_the_character_has_left_the_connection_before_the_target_instance_is_ready()
    {
        CharacterEntity character = New(7);
        MapInstance source = Source();
        character.InstanceId = source.InstanceId;
        character.Position = Vector3.zero;

        IInstanceRegistry registry = Substitute.For<IInstanceRegistry>();
        registry.GetInstanceById(source.InstanceId).Returns(source);
        IMapInstance target = Substitute.For<IMapInstance>();
        registry.GetOrCreateTownInstanceAsync(Arg.Any<MapTemplateId>(), Arg.Any<ushort>()).Returns(target);

        IWorld world = Substitute.For<IWorld>();
        world.InstanceRegistry.Returns(registry);
        world.MapTemplates.Returns(new List<MapTemplate>
        {
            new() { Id = new MapTemplateId(TargetMap), MapType = MapType.Town, Name = "target", Description = "" }
        });

        IWorldConnection connection = Substitute.For<IWorldConnection, ISettledContinuations>();
        connection.InGame.Returns(true);
        connection.Character.Returns(character);
        Action<Task<IMapInstance>>? onInstance = null;
        ((ISettledContinuations)connection)
            .When(c => c.EnqueueSettled(Arg.Any<Task<IMapInstance>>(), Arg.Any<Action<Task<IMapInstance>>>()))
            .Do(call => onInstance = call.Arg<Action<Task<IMapInstance>>>());

        ICharacterSaver saver = Substitute.For<ICharacterSaver>();
        var handler = new EnterMapHandler(NullLogger<EnterMapHandler>.Instance, saver, world);

        handler.Execute(connection, new CEnterMapPacket { TargetMapId = TargetMap });
        Assert.NotNull(onInstance);

        // The connection cleared the move as the character left it, and the one selected since has a move of its own
        // under way: that flag is not this arrival's to clear.
        connection.Character.Returns((ICharacter?)null);
        connection.RespawnInFlight = true;

        Exception? escaped = Record.Exception(() => onInstance!(Task.FromResult(target)));

        Assert.Null(escaped);
        world.DidNotReceiveWithAnyArgs().TransferPlayer(default!, default!);
        saver.DidNotReceiveWithAnyArgs().Save(default!, default!);
        Assert.True(connection.RespawnInFlight);
    }

    /// <summary>
    /// A scroll return, an item teleport, a respawn or a party return under way decides where the character goes, so a
    /// portal entry meanwhile is answered MoveInProgress before anything is looked up or built.
    /// </summary>
    [Fact]
    public void Refuse_a_portal_entry_while_another_move_is_under_way()
    {
        CharacterEntity character = New(7);
        MapInstance source = Source();
        character.InstanceId = source.InstanceId;
        character.Position = Vector3.zero;

        IInstanceRegistry registry = Substitute.For<IInstanceRegistry>();
        registry.GetInstanceById(source.InstanceId).Returns(source);
        IWorld world = Substitute.For<IWorld>();
        world.InstanceRegistry.Returns(registry);
        world.MapTemplates.Returns(new List<MapTemplate>
        {
            new() { Id = new MapTemplateId(TargetMap), MapType = MapType.Town, Name = "target", Description = "" }
        });

        IWorldConnection connection = Substitute.For<IWorldConnection, ISettledContinuations>();
        connection.InGame.Returns(true);
        var sent = new List<OutboundPacket>();
        TestTown.Record(connection, character, sent);
        connection.RespawnInFlight.Returns(true);

        var handler = new EnterMapHandler(NullLogger<EnterMapHandler>.Instance, Substitute.For<ICharacterSaver>(),
            world);
        handler.Execute(connection, new CEnterMapPacket { TargetMapId = TargetMap });

        Assert.Equal(MapTransitionResult.MoveInProgress,
            Assert.Single(TestTown.Read<SMapTransitionPacket>(sent, NetworkPacketType.SMSG_MAP_TRANSITION)).Result);
        ((ISettledContinuations)connection).DidNotReceiveWithAnyArgs().EnqueueSettled(default(Task<IMapInstance>)!, default!);
        registry.DidNotReceiveWithAnyArgs().GetInstanceById(default);
        _ = world.DidNotReceive().MapTemplates;
    }

    /// <summary>
    /// A request the handler accepts is a move under way until it ends. Continuations run after the packet pass, so
    /// without that a second request in the same pass would pass every check the first one passed and arrive, and
    /// save, once more. Once the first has arrived the move is over, and the next request is taken.
    /// </summary>
    [Fact]
    public void Refuse_a_second_portal_entry_while_the_first_is_under_way_and_take_the_next_once_it_has_arrived()
    {
        var entry = new Entry();

        entry.Request();
        entry.Request(); // in the same pass: the first one's continuation has not run yet
        entry.Flush();

        entry.World.Received(1).TransferPlayer(entry.Connection, entry.Target);
        entry.Saver.Received(1).Save(entry.Connection, entry.Character);
        Assert.Equal([MapTransitionResult.MoveInProgress, MapTransitionResult.Success], entry.Results());
        Assert.False(entry.Connection.RespawnInFlight);

        entry.StandOnThePortal(); // the arrival moved the character to the target
        entry.Request();
        entry.Flush();

        entry.World.Received(2).TransferPlayer(entry.Connection, entry.Target);
        Assert.Equal(MapTransitionResult.Success, entry.Results()[^1]);
    }

    /// <summary>
    /// The character arrives at the target's entry spawn, at the far end of its layout from where it stood, and the
    /// target's spatial index has it there (#982): its own client lists it on its first tick there, and so does a client
    /// standing at the spawn. A position written to the character's row alone moves the character without its index entry,
    /// which stays where it stood, out of reach of every view around the spawn, its own included.
    /// </summary>
    [Fact]
    public void Arrive_where_the_target_s_spatial_index_has_the_character()
    {
        PlacedChunk[] chunks = Enumerable.Range(0, 10)
            .Select(x => new PlacedChunk(new ChunkTemplateId(1), (short)x, 0, 0, new Vector3(x * 30f, 0f, 0f)))
            .ToArray();
        var spawn = new Vector3(285f, 0f, 15f);
        var layout = new ChunkLayout(0, chunks, chunks[0], null, [], spawn, 30f);
        using MapInstance target = TestMapInstances.Build(MapInstanceClients.NewWorld(), layout: layout);
        var entry = new Entry(target: target);
        entry.Character.Spells.Load(Array.Empty<IAbility>());   // the tick updates abilities; an unloaded list throws
        entry.World.When(w => w.TransferPlayer(entry.Connection, target)).Do(_ =>
        {
            entry.Character.InstanceId = target.InstanceId;
            target.AddCharacter(entry.Connection);
        });
        MapInstanceClient there = MapInstanceClients.Join(target, New(8));
        there.Character.Position = spawn;

        entry.Request();
        entry.Flush();
        target.Update(TimeSpan.FromSeconds(1d / 60d));

        Assert.Equal(MapTransitionResult.Success, entry.Results()[^1]);
        Assert.Equal(spawn, entry.Character.Position);
        Assert.Contains(entry.Character.Guid.RawValue, entry.Added());
        Assert.Contains(entry.Character.Guid.RawValue, there.Added().Select(s => s.Guid));
    }

    public enum Ending
    {
        TheBuildFaults,
        TheBuildFaultsAndItsLogLineThrows,
        TheBuildIsCancelled,
        TheRequestForTheInstanceThrows,
        TheInstanceIsReleasedTwice,
        TheInstanceIsReleasedAndTheSecondRequestThrows,
        TheTransferThrows,
        TheCharacterDiesWhileItsInstanceIsBuilt,
        TheBuildFaultsAndTheCharacterDied,
        TheBuildFaultsAndTheCharacterLeft,
    }

    /// <summary>
    /// A portal entry that ends without arriving still ends: the move is no longer under way, so the character's next
    /// portal entry, return to town or teleport is not refused for the rest of its session.
    /// </summary>
    [Theory]
    [InlineData(Ending.TheBuildFaults)]
    [InlineData(Ending.TheBuildFaultsAndItsLogLineThrows)]
    [InlineData(Ending.TheBuildIsCancelled)]
    [InlineData(Ending.TheRequestForTheInstanceThrows)]
    [InlineData(Ending.TheInstanceIsReleasedTwice)]
    [InlineData(Ending.TheInstanceIsReleasedAndTheSecondRequestThrows)]
    [InlineData(Ending.TheTransferThrows)]
    [InlineData(Ending.TheCharacterDiesWhileItsInstanceIsBuilt)]
    [InlineData(Ending.TheBuildFaultsAndTheCharacterDied)]
    [InlineData(Ending.TheBuildFaultsAndTheCharacterLeft)]
    public void Take_a_later_portal_entry_after_one_that_ended_without_arriving(Ending ending)
    {
        // The failed build's Error line is written on the tick, in the arrival, by a logger that rethrows what its
        // providers throw.
        var log = new TestLog();
        var entry = new Entry(log: ending == Ending.TheBuildFaultsAndItsLogLineThrows
            ? new ThrowingLog()
            : new Logger<EnterMapHandler>(log));
        bool failing = true;
        switch (ending)
        {
            case Ending.TheBuildFaults or Ending.TheBuildFaultsAndItsLogLineThrows
                or Ending.TheBuildFaultsAndTheCharacterDied or Ending.TheBuildFaultsAndTheCharacterLeft:
                entry.Registry.GetOrCreateTownInstanceAsync(Arg.Any<MapTemplateId>(), Arg.Any<ushort>()).Returns(
                    Task.FromException<IMapInstance>(new InvalidOperationException("bake failed")),
                    Task.FromResult(entry.Target));
                break;
            case Ending.TheBuildIsCancelled:
                entry.Registry.GetOrCreateTownInstanceAsync(Arg.Any<MapTemplateId>(), Arg.Any<ushort>()).Returns(
                    Task.FromCanceled<IMapInstance>(new CancellationToken(canceled: true)),
                    Task.FromResult(entry.Target));
                break;
            case Ending.TheRequestForTheInstanceThrows:
                entry.Registry.GetOrCreateTownInstanceAsync(Arg.Any<MapTemplateId>(), Arg.Any<ushort>()).Returns(
                    _ => failing ? throw new InvalidOperationException("no registry") : Task.FromResult(entry.Target));
                break;
            case Ending.TheInstanceIsReleasedTwice:
                entry.Registry.GetInstanceById(entry.Target.InstanceId).Returns(_ => failing ? null : entry.Target);
                break;
            case Ending.TheInstanceIsReleasedAndTheSecondRequestThrows:
                // The arrival finds its instance released and asks for it again, and that second ask throws: the
                // move was never handed on, so the arrival that threw is the one that ends it.
                int asked = 0;
                entry.Registry.GetInstanceById(entry.Target.InstanceId).Returns(_ => failing ? null : entry.Target);
                entry.Registry.GetOrCreateTownInstanceAsync(Arg.Any<MapTemplateId>(), Arg.Any<ushort>()).Returns(
                    _ => failing && ++asked == 2
                        ? throw new InvalidOperationException("no registry")
                        : Task.FromResult(entry.Target));
                break;
            case Ending.TheTransferThrows:
                entry.World.When(w => w.TransferPlayer(entry.Connection, entry.Target)).Do(_ =>
                {
                    if (failing)
                        throw new InvalidOperationException("transfer failed");
                });
                break;
        }

        // The dispatch contains what the handler throws, and the continuation drain what an arrival throws.
        Record.Exception(() => entry.Request());
        if (ending is Ending.TheCharacterDiesWhileItsInstanceIsBuilt or Ending.TheBuildFaultsAndTheCharacterDied)
            entry.Character.IsDead = true; // asked alive; dead by the time the instance is ready
        if (ending == Ending.TheBuildFaultsAndTheCharacterLeft)
            entry.Connection.Character.Returns((ICharacter?)null); // a select of it elsewhere took it
        entry.Flush();

        // A failed build is logged whatever became of the character: nobody else logs it.
        if (ending is Ending.TheBuildFaultsAndTheCharacterDied or Ending.TheBuildFaultsAndTheCharacterLeft)
            Assert.Single(log.Errors, e => e.Exception?.InnerException is InvalidOperationException);

        if (ending == Ending.TheBuildFaultsAndTheCharacterLeft)
        {
            // Not this arrival's to clear: the connection cleared it as the character left (ResetCharacterState).
            Assert.True(entry.Connection.RespawnInFlight);
            return;
        }

        if (ending == Ending.TheInstanceIsReleasedTwice)
        {
            // Released once: the arrival resolved the target again, and the move is that second resolve now.
            Assert.True(entry.Connection.RespawnInFlight);
            entry.Flush();
        }

        Assert.DoesNotContain(MapTransitionResult.Success, entry.Results());
        if (ending == Ending.TheCharacterDiesWhileItsInstanceIsBuilt)
            entry.World.DidNotReceiveWithAnyArgs().TransferPlayer(default!, default!);
        Assert.False(entry.Connection.RespawnInFlight);

        failing = false;
        entry.Character.Revive();
        entry.Request();
        entry.Flush();

        Assert.Equal(MapTransitionResult.Success, entry.Results()[^1]);
        Assert.False(entry.Connection.RespawnInFlight);
    }

    /// <summary>
    /// The refusals log at Debug, which a world does not write as shipped, so <c>world.portal_entry.refusals</c> is what
    /// shows them: one count per refusal, under the reason of the place that refused, and none for an entry that arrives.
    /// </summary>
    [Theory]
    [InlineData("dead")]
    [InlineData("move_in_progress")]
    [InlineData("unknown_map")]
    [InlineData("no_instance")]
    [InlineData("no_portal")]
    [InlineData("not_near_portal")]
    [InlineData("level_too_low")]
    [InlineData("level_too_high")]
    [InlineData("instance_released")]
    [InlineData("not_in_party")]
    [InlineData("instance_full")]
    [InlineData(null)]
    public void Count_a_refused_portal_entry_once_under_its_reason(string? reason)
    {
        PartyTestWorld? parties = reason is "not_in_party" or "instance_full" ? new PartyTestWorld() : null;
        var entry = new Entry(parties: parties);
        ushort map = TargetMap;
        switch (reason)
        {
            case "dead":
                entry.Character.IsDead = true;
                break;
            case "move_in_progress":
                entry.Connection.RespawnInFlight = true;
                break;
            case "unknown_map":
                map = 99;
                break;
            case "no_instance":
                entry.Character.InstanceId = Guid.NewGuid();
                break;
            case "no_portal":
                map = Entry.MapWithoutPortal;
                break;
            case "not_near_portal":
                entry.Character.Position = new Vector3(40f, 0f, 0f);
                break;
            case "level_too_low":
                entry.Template.MinLevel = (ushort)(entry.Character.Level + 1);
                break;
            case "level_too_high":
                entry.Character.Level = 5;
                entry.Template.MaxLevel = 4;
                break;
            case "instance_released":
                entry.Registry.GetInstanceById(entry.Target.InstanceId).Returns((IMapInstance?)null);
                break;
            case "instance_full":
                entry.Template.MaxPlayers = 1;
                entry.Target.PlayerCount.Returns(1);
                break;
        }

        using var counted = new RefusalProbe();
        entry.Request(map);
        if (reason == "not_in_party")
            parties!.Parties.Leave(entry.Character.Guid.Id); // the party is gone before its instance is ready
        entry.Flush();
        entry.Flush(); // an instance released before the arrival is resolved again, and refused the second time

        Assert.Equal(reason is null ? [] : [reason], counted.Reasons);
        Assert.Equal(reason is null, entry.Results().Contains(MapTransitionResult.Success));
    }

    /// <summary>
    /// The reasons counted in <c>world.portal_entry.refusals</c> on this test's own flow: the counter is static and
    /// shared, and other test classes refuse portal entries in parallel.
    /// </summary>
    private sealed class RefusalProbe : IDisposable
    {
        private static readonly AsyncLocal<RefusalProbe?> s_current = new();
        private readonly MeterListener _listener = new();

        public RefusalProbe()
        {
            s_current.Value = this;
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter == DiagnosticsConfig.World.Meter && instrument.Name == "world.portal_entry.refusals")
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            {
                if (s_current.Value != this)
                    return;

                foreach (KeyValuePair<string, object?> tag in tags)
                {
                    if (tag.Key == "reason")
                        Reasons.Add((string)tag.Value!);
                }
            });
            _listener.Start();
        }

        public List<string> Reasons { get; } = [];

        public void Dispose() => _listener.Dispose();
    }

    /// <summary>A log whose Error lines throw, as a provider that fails does through the logger that calls it.</summary>
    private sealed class ThrowingLog : ILogger<EnterMapHandler>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                throw new InvalidOperationException("the log provider failed");
        }
    }

    /// <summary>
    /// A character standing on a portal to the town, behind a connection that records what it is sent and queues the
    /// arrivals it is handed for <see cref="Flush" />, which runs them as the connection's drain runs a continuation
    /// queued whatever its task's outcome (<see cref="ISettledContinuations" />), once the packet pass is over: when
    /// the task has ended, succeeded or not, with a callback that throws contained. An arrival handed to the ordinary
    /// queue instead, whose drain drops the callback of a task that failed, is never run here. Given a party world,
    /// the character leads a party of two and the portal leads to a Normal map, so to the party's instance of it.
    /// </summary>
    private sealed class Entry
    {
        private readonly List<(Task<IMapInstance> Task, Action<Task<IMapInstance>> Callback)> _queued = [];
        private readonly List<OutboundPacket> _sent = [];
        private readonly EnterMapHandler _handler;
        private readonly MapInstance _source = Source();

        /// <param name="target">The instance the portal leads to; a substitute when omitted.</param>
        public Entry(ILogger<EnterMapHandler>? log = null, PartyTestWorld? parties = null, MapInstance? target = null)
        {
            StandOnThePortal();

            if (target is null)
                Target.InstanceId.Returns(Guid.NewGuid());
            else
                Target = target;
            Registry.GetInstanceById(_source.InstanceId).Returns(_source);
            Registry.GetInstanceById(Target.InstanceId).Returns(Target);
            Registry.GetOrCreateTownInstanceAsync(Arg.Any<MapTemplateId>(), Arg.Any<ushort>()).Returns(Task.FromResult(Target));

            World.InstanceRegistry.Returns(Registry);
            World.MapTemplates.Returns(new List<MapTemplate>
            {
                Template,
                new() { Id = new MapTemplateId(MapWithoutPortal), MapType = MapType.Town, Name = "elsewhere", Description = "" },
            });

            Connection.InGame.Returns(true);
            TestTown.Record(Connection, Character, _sent);
            ((ISettledContinuations)Connection)
                .When(c => c.EnqueueSettled(Arg.Any<Task<IMapInstance>>(), Arg.Any<Action<Task<IMapInstance>>>()))
                .Do(call => _queued.Add((call.Arg<Task<IMapInstance>>(), call.Arg<Action<Task<IMapInstance>>>())));

            if (parties is not null)
            {
                Connection.AccountId.Returns(new AccountId(7));
                parties.Parties.CharacterOnline(Connection);
                PartyClient other = parties.Online(8);
                Assert.Equal(PartyResult.Ok, parties.Parties.Invite(Character.Guid.Id, other.Character.Name));
                Assert.Equal(PartyResult.Ok, parties.Parties.Respond(other.Id, accept: true));
                _sent.Clear();

                IPartyInstanceRegistry partyInstances = Substitute.For<IPartyInstanceRegistry>();
                partyInstances.GetOrCreatePartyInstanceAsync(Arg.Any<PartyId>(), Arg.Any<MapTemplateId>())
                    .Returns(Task.FromResult(Target));
                World.PartyInstances.Returns(partyInstances);
                World.Configuration.Returns(parties.Config);
                Template.MapType = MapType.Normal;
            }

            _handler = new EnterMapHandler(log ?? NullLogger<EnterMapHandler>.Instance, Saver, World, parties?.Parties);
        }

        /// <summary>A map the world has and the source instance has no portal to.</summary>
        public const ushort MapWithoutPortal = 3;

        public CharacterEntity Character { get; } = New(7);

        /// <summary>The town the portal leads to.</summary>
        public MapTemplate Template { get; } =
            new() { Id = new MapTemplateId(TargetMap), MapType = MapType.Town, Name = "target", Description = "" };
        public IMapInstance Target { get; } = Substitute.For<IMapInstance>();

        /// <summary>The guid of every object the connection was told is in view, in order.</summary>
        public List<ulong> Added() => TestTown.Read<SInstanceStateAddPacket>(_sent, NetworkPacketType.SMSG_WORLD_STATE_ADD)
            .SelectMany(p => p.Adds ?? [])
            .Select(s => s.Guid)
            .ToList();
        public IInstanceRegistry Registry { get; } = Substitute.For<IInstanceRegistry>();
        public IWorld World { get; } = Substitute.For<IWorld>();
        public IWorldConnection Connection { get; } = Substitute.For<IWorldConnection, ISettledContinuations>();
        public ICharacterSaver Saver { get; } = Substitute.For<ICharacterSaver>();

        public void Request(ushort map = TargetMap) => _handler.Execute(Connection, new CEnterMapPacket { TargetMapId = map });

        /// <summary>Puts the character in the source instance, on its portal to the town.</summary>
        public void StandOnThePortal()
        {
            Character.InstanceId = _source.InstanceId;
            Character.Position = Vector3.zero;
        }

        /// <summary>Runs the continuations queued so far, as one tick's flush; any they queue wait for the next call.</summary>
        public void Flush()
        {
            List<(Task<IMapInstance> Task, Action<Task<IMapInstance>> Callback)> due = [.. _queued];
            _queued.Clear();
            foreach ((Task<IMapInstance> task, Action<Task<IMapInstance>> callback) in due)
            {
                if (task.IsCompleted)
                    Record.Exception(() => callback(task));
                else
                    _queued.Add((task, callback));
            }
        }

        /// <summary>The result of every transition the connection was sent, in order.</summary>
        public List<MapTransitionResult> Results() =>
            TestTown.Read<SMapTransitionPacket>(_sent, NetworkPacketType.SMSG_MAP_TRANSITION).ConvertAll(t => t.Result);
    }

    /// <summary>A real instance with a layout and a portal to the target map at the origin, so Execute reaches the continuation.</summary>
    private static MapInstance Source()
    {
        IServiceProvider serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IScriptManager)).Returns(Substitute.For<IScriptManager>());
        serviceProvider.GetService(typeof(CombatConfig)).Returns(new CombatConfig());
        serviceProvider.GetService(typeof(IChunkLibrary)).Returns(new AnyChunkLibrary());

        IWorld world = Substitute.For<IWorld>();
        world.Configuration.Returns(new GameConfiguration());

        var entryChunk = new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero);
        var layout = new ChunkLayout(Seed: 0, Chunks: [entryChunk], EntryChunk: entryChunk, BossChunk: null,
            Portals: [], EntrySpawnWorldPos: Vector3.zero, CellSize: 30f, Config: null);

        var source = new MapInstance(NullLoggerFactory.Instance, serviceProvider, world, new MapTemplateId(TownMap),
            ownerCharacterId: null, layout, Substitute.For<IMapNavigator>(), seed: 0);
        source.AddPortal(new PortalInstance(new ObjectGuid(ObjectType.Portal, 1), Vector3.zero, 5f, TargetMap, 1));
        return source;
    }
}
