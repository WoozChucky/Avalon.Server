using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Respawn;
using NSubstitute;
using NSubstitute.ClearExtensions;
using Xunit;

namespace Avalon.Server.World.UnitTests.Respawn;

public class TownReturnShould
{
    private readonly IWorldConnection _connection = Substitute.For<IWorldConnection>();
    private readonly ICharacter _character = Substitute.For<ICharacter>();
    private readonly IWorld _world = Substitute.For<IWorld>();
    private readonly IMapInstance _source = Substitute.For<IMapInstance>();
    private readonly IMapInstance _townInstance = Substitute.For<IMapInstance>();
    private readonly IRespawnTargetResolver _resolver = Substitute.For<IRespawnTargetResolver>();
    private readonly IInstanceRegistry _registry = Substitute.For<IInstanceRegistry>();
    private readonly TestLog _log = new();
    private readonly TownReturn _town;

    public TownReturnShould()
    {
        var sourceId = Guid.NewGuid();
        _character.Map.Returns(new MapId(2));
        _character.InstanceId.Returns(sourceId);
        _character.Name.Returns("Tester");

        _connection.Character.Returns(_character);
        _connection.CryptoSession.Returns(new FakeAvalonCryptoSession());
        // Both continuations run inline, as the tick would run them once their tasks complete.
        _connection.When(c => c.EnqueueContinuation(Arg.Any<Task>(), Arg.Any<Action>()))
            .Do(ci => ci.Arg<Action>()());

        ICombatService combat = Substitute.For<ICombatService>();
        _source.CombatService.Returns(combat);
        _source.InstanceId.Returns(sourceId);
        _townInstance.InstanceId.Returns(Guid.NewGuid());

        _registry.GetInstanceById(sourceId).Returns(_source);
        _registry.GetOrCreateTownInstanceAsync(Arg.Any<MapTemplateId>(), Arg.Any<ushort>())
            .Returns(Task.FromResult(_townInstance));

        _world.InstanceRegistry.Returns(_registry);
        _world.MapTemplates.Returns(new List<MapTemplate>
        {
            new() { Id = new MapTemplateId(1), MapType = MapType.Town, Name = "town", Description = "" },
        });

        _resolver.ResolveTownAsync(Arg.Any<MapTemplateId>(), Arg.Any<CancellationToken>())
            .Returns(new MapTemplateId(1));

        _town = new TownReturn(_log, _world, _resolver, Substitute.For<IChunkLibrary>());
        _connection.RespawnInFlight = true;
    }

    [Fact]
    public void Move_a_living_character_to_town_without_reviving_it_and_drop_it_from_its_encounter()
    {
        _town.Start(_connection, revive: false, dropEncounter: true);

        _source.CombatService.Received(1).DropPlayerFromEncounter(_character);
        _world.Received(1).TransferPlayer(_connection, _townInstance);
        _character.DidNotReceive().Revive();
        Assert.False(_connection.RespawnInFlight);
    }

    [Fact]
    public void Revive_a_dead_character_it_moves_when_asked()
    {
        _town.Start(_connection, revive: true, dropEncounter: false);

        _character.Received(1).Revive();
        _source.CombatService.DidNotReceiveWithAnyArgs().DropPlayerFromEncounter(default!);
        _world.Received(1).TransferPlayer(_connection, _townInstance);
    }

    /// <summary>
    /// A member alive when its countdown ran out who dies before the town is ready arrives alive, as a respawn does.
    /// </summary>
    [Fact]
    public void Revive_a_character_that_died_before_it_arrived()
    {
        _character.IsDead.Returns(true);

        _town.Start(_connection, revive: false, dropEncounter: true);

        _character.Received(1).Revive();
        Assert.False(_connection.RespawnInFlight);
    }

    /// <summary>
    /// The town lookup reads the database. A faulted lookup used to have its callback dropped by the connection's
    /// continuation drain, leaving RespawnInFlight set for good: a later death could never respawn.
    /// </summary>
    [Fact]
    public void Clear_the_flag_and_log_when_the_town_lookup_faults()
    {
        _resolver.ResolveTownAsync(Arg.Any<MapTemplateId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<MapTemplateId>(new InvalidOperationException("database down")));

        _town.Start(_connection, revive: false, dropEncounter: true);

        Assert.False(_connection.RespawnInFlight);
        Assert.Contains(_log.Errors, e => e.Exception is InvalidOperationException);
        _world.DidNotReceiveWithAnyArgs().TransferPlayer(default!, default!);
    }

    [Fact]
    public void Clear_the_flag_and_log_when_the_town_lookup_throws()
    {
        _resolver.ResolveTownAsync(Arg.Any<MapTemplateId>(), Arg.Any<CancellationToken>())
            .Returns<Task<MapTemplateId>>(_ => throw new InvalidOperationException("database down"));

        _town.Start(_connection, revive: false, dropEncounter: true);

        Assert.False(_connection.RespawnInFlight);
        Assert.Contains(_log.Errors, e => e.Exception is InvalidOperationException);
    }

    [Fact]
    public void Clear_the_flag_and_log_when_the_town_instance_build_faults()
    {
        _registry.GetOrCreateTownInstanceAsync(Arg.Any<MapTemplateId>(), Arg.Any<ushort>())
            .Returns(Task.FromException<IMapInstance>(new InvalidOperationException("build failed")));

        _town.Start(_connection, revive: false, dropEncounter: true);

        Assert.False(_connection.RespawnInFlight);
        Assert.Contains(_log.Errors, e => e.Exception is InvalidOperationException);
    }

    /// <summary>A step on arrival that throws still clears the flag: it is cleared in a finally, and the throw logged.</summary>
    [Fact]
    public void Clear_the_flag_and_log_when_a_step_on_arrival_throws()
    {
        _world.When(w => w.TransferPlayer(_connection, _townInstance))
            .Do(_ => throw new InvalidOperationException("transfer failed"));

        Exception? escaped = Record.Exception(() => _town.Start(_connection, revive: false, dropEncounter: true));

        Assert.Null(escaped);
        Assert.False(_connection.RespawnInFlight);
        Assert.Contains(_log.Errors, e => e.Exception is InvalidOperationException);
    }

    /// <summary>After a character leave the flag belongs to the next character on the connection: a stale failure leaves it alone.</summary>
    [Fact]
    public void Leave_the_flag_alone_when_the_character_left_before_the_lookup_failed()
    {
        var lookup = new TaskCompletionSource<MapTemplateId>();
        _resolver.ResolveTownAsync(Arg.Any<MapTemplateId>(), Arg.Any<CancellationToken>()).Returns(lookup.Task);
        Action? pending = null;
        _connection.ClearSubstitute(ClearOptions.CallActions);   // hold the continuation instead of running it inline
        _connection.When(c => c.EnqueueContinuation(Arg.Any<Task>(), Arg.Any<Action>()))
            .Do(ci => pending = ci.Arg<Action>());

        _town.Start(_connection, revive: false, dropEncounter: true);
        _connection.Character.Returns(Substitute.For<ICharacter>());
        lookup.SetException(new InvalidOperationException("database down"));
        pending!();

        Assert.True(_connection.RespawnInFlight);
        Assert.Contains(_log.Errors, e => e.Exception is InvalidOperationException);
    }

    /// <summary>#700: a caller that retries takes the failure itself, after the flag is cleared; nothing is logged at Error here.</summary>
    [Fact]
    public void Hand_a_failure_to_the_callers_handler_instead_of_logging_it()
    {
        _registry.GetOrCreateTownInstanceAsync(Arg.Any<MapTemplateId>(), Arg.Any<ushort>())
            .Returns(Task.FromException<IMapInstance>(new InvalidOperationException("build failed")));
        var handed = new List<(Exception Failure, bool FlagWhenHanded)>();

        _town.Start(_connection, revive: false, dropEncounter: true,
            failed: e => handed.Add((e, _connection.RespawnInFlight)));

        var (failure, flagWhenHanded) = Assert.Single(handed);
        Assert.IsType<InvalidOperationException>(failure);
        Assert.False(flagWhenHanded);
        Assert.Empty(_log.Errors);
    }

    [Fact]
    public void Not_call_the_failure_handler_for_a_return_that_arrived()
    {
        bool called = false;

        _town.Start(_connection, revive: false, dropEncounter: true, failed: _ => called = true);

        Assert.False(called);
        _world.Received(1).TransferPlayer(_connection, _townInstance);
    }

    [Fact]
    public void Log_a_failure_handler_that_throws()
    {
        _resolver.ResolveTownAsync(Arg.Any<MapTemplateId>(), Arg.Any<CancellationToken>())
            .Returns<Task<MapTemplateId>>(_ => throw new InvalidOperationException("database down"));

        Exception? escaped = Record.Exception(() => _town.Start(_connection, revive: false, dropEncounter: true,
            failed: _ => throw new NotSupportedException("handler broke")));

        Assert.Null(escaped);
        Assert.False(_connection.RespawnInFlight);
        Assert.Contains(_log.Errors, e => e.Exception is AggregateException);
    }
}
