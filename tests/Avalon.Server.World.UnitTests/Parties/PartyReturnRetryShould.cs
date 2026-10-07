using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Parties;
using Avalon.World.Public;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Respawn;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Parties;

/// <summary>
/// #700: a leave countdown's return to town that fails (the town lookup or build faults, or a step throws) is tried
/// again every PartyReturnRetrySeconds, at most PartyService.MaxReturnRetries times, while the character is still in
/// the instance its countdown was for and not back in that party.
/// </summary>
public class PartyReturnRetryShould
{
    private static readonly Guid PartyInstance = Guid.NewGuid();
    private static readonly MapTemplateId TownMap = new(1);

    private readonly TestLog _log = new();
    private readonly PartyTestWorld _w;
    private readonly PartyClient _a;
    private readonly PartyClient _b;
    private readonly PartyClient _c;
    private readonly IWorld _world = Substitute.For<IWorld>();
    private readonly IRespawnTargetResolver _resolver = Substitute.For<IRespawnTargetResolver>();
    private readonly IMapInstance _townInstance = Substitute.For<IMapInstance>();
    private readonly TownReturn _town;
    private int _lookups;
    private int _failuresLeft;

    public PartyReturnRetryShould()
    {
        _w = new PartyTestWorld(logger: new Logger<PartyService>(_log));
        _a = _w.Online(1, "A", instance: PartyInstance);
        _b = _w.Online(2, "B", instance: PartyInstance);
        _c = _w.Online(3, "C", instance: PartyInstance);
        _w.Form(_a, _b, _c);
        _w.Instances.Owned[PartyInstance] = _w.Parties.PartyOf(_a.Id)!.Id;

        // Continuations run inline, as the tick runs them once their tasks complete.
        _c.Connection.When(c => c.EnqueueContinuation(Arg.Any<Task>(), Arg.Any<Action>()))
            .Do(ci => ci.Arg<Action>()());

        _townInstance.InstanceId.Returns(Guid.NewGuid());
        IInstanceRegistry registry = Substitute.For<IInstanceRegistry>();
        registry.GetOrCreateTownInstanceAsync(Arg.Any<MapTemplateId>(), Arg.Any<ushort>()).Returns(Task.FromResult(_townInstance));
        _world.InstanceRegistry.Returns(registry);
        _world.MapTemplates.Returns(new List<MapTemplate>
        {
            new() { Id = TownMap, MapType = MapType.Town, Name = "town", Description = "" },
        });
        // What World.TransferPlayer does that matters here: the character is in the town, and the party is told.
        _world.When(w => w.TransferPlayer(Arg.Any<IWorldConnection>(), _townInstance)).Do(ci =>
        {
            IWorldConnection connection = ci.Arg<IWorldConnection>();
            connection.Character!.InstanceId = _townInstance.InstanceId;
            _w.Parties.InstanceChanged(connection);
        });

        _resolver.ResolveTownAsync(Arg.Any<MapTemplateId>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _lookups++;
            return _failuresLeft-- > 0
                ? Task.FromException<MapTemplateId>(new InvalidOperationException("database down"))
                : Task.FromResult(TownMap);
        });

        _town = new TownReturn(_log, _world, _resolver, Substitute.For<IChunkLibrary>());
    }

    /// <summary>What World.Update does each tick: the party tick, then the returns it hands over.</summary>
    private void After(int seconds)
    {
        _w.Clock.Advance(TimeSpan.FromSeconds(seconds));
        _w.Parties.StartReturns(_w.Parties.Tick(), _town);
    }

    private IEnumerable<(LogLevel Level, Exception? Exception, string Message)> Warnings =>
        _log.Entries.Where(e => e.Level == LogLevel.Warning);

    [Fact]
    public void Retry_a_failed_return_after_the_retry_delay()
    {
        _failuresLeft = 1;
        _w.Parties.Leave(_c.Id);
        _c.Clear();

        After(60);

        Assert.Equal(1, _lookups);
        Assert.Equal(PartyInstance, _c.Character.InstanceId);
        Assert.False(_c.Connection.RespawnInFlight);
        Assert.True(_w.Parties.InCountdown(_c.Id));
        Assert.Contains(Warnings, e => e.Exception is InvalidOperationException);
        Assert.Empty(_log.Errors);

        After(4);
        Assert.Equal(1, _lookups);

        After(1);

        Assert.Equal(2, _lookups);
        _world.Received(1).TransferPlayer(_c.Connection, _townInstance);
        Assert.Equal(_townInstance.InstanceId, _c.Character.InstanceId);
        Assert.False(_c.Connection.RespawnInFlight);
        Assert.False(_w.Parties.InCountdown(_c.Id));
        Assert.Empty(_log.Errors);
        Assert.Empty(_c.Lines()); // a retry says nothing: the countdown already reached zero

        After(60);
        Assert.Equal(2, _lookups);
    }

    [Fact]
    public void Honour_the_configured_retry_delay()
    {
        var w = new PartyTestWorld(c => c.PartyReturnRetrySeconds = 12);
        PartyClient a = w.Online(1, "A", instance: PartyInstance);
        PartyClient b = w.Online(2, "B", instance: PartyInstance);
        PartyClient c = w.Online(3, "C", instance: PartyInstance);
        w.Form(a, b, c);
        w.Instances.Owned[PartyInstance] = w.Parties.PartyOf(a.Id)!.Id;
        w.Parties.Leave(b.Id);
        w.Clock.Advance(TimeSpan.FromSeconds(60));
        Assert.Single(w.Parties.Tick());

        w.Parties.ReturnFailed(b.Id, new InvalidOperationException("database down"));

        w.Clock.Advance(TimeSpan.FromSeconds(11));
        Assert.Empty(w.Parties.Tick());
        w.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Same(b.Connection, Assert.Single(w.Parties.Tick()));
    }

    [Fact]
    public void Give_up_after_the_retry_cap_and_log_it()
    {
        _failuresLeft = int.MaxValue;
        _w.Parties.Leave(_c.Id);

        After(60);
        for (int i = 0; i < PartyService.MaxReturnRetries; i++)
            After(5);

        Assert.Equal(PartyService.MaxReturnRetries + 1, _lookups);
        Assert.Equal(PartyService.MaxReturnRetries, Warnings.Count());
        var gaveUp = Assert.Single(_log.Errors);
        Assert.Contains("Gave up", gaveUp.Message, StringComparison.Ordinal);
        Assert.False(_w.Parties.InCountdown(_c.Id));
        Assert.False(_c.Connection.RespawnInFlight);
        Assert.Equal(PartyInstance, _c.Character.InstanceId);

        After(60);
        Assert.Equal(PartyService.MaxReturnRetries + 1, _lookups);
    }

    [Fact]
    public void Stop_retrying_once_the_character_left_the_instance()
    {
        _failuresLeft = int.MaxValue;
        _w.Parties.Leave(_c.Id);
        After(60);

        _c.Character.InstanceId = Guid.NewGuid(); // a portal, or a respawn in town
        After(5);

        Assert.Equal(1, _lookups);
        Assert.False(_w.Parties.InCountdown(_c.Id));
    }

    [Fact]
    public void Stop_retrying_once_the_character_rejoined_the_party()
    {
        _failuresLeft = int.MaxValue;
        _w.Parties.Leave(_c.Id);
        After(60);

        _w.Parties.Invite(_a.Id, "C");
        _w.Parties.Respond(_c.Id, accept: true);
        After(5);

        Assert.Equal(1, _lookups);
        Assert.False(_w.Parties.InCountdown(_c.Id));
    }

    [Fact]
    public void Stop_retrying_once_the_character_went_offline()
    {
        _failuresLeft = int.MaxValue;
        _w.Parties.Leave(_c.Id);
        After(60);

        _w.Parties.CharacterOffline(_c.Connection, _c.Character);
        After(5);

        Assert.Equal(1, _lookups);
        Assert.False(_w.Parties.InCountdown(_c.Id));
    }

    /// <summary>A failure reported once the character is elsewhere (it rejoined, or walked out, while the return ran) is not retried.</summary>
    [Fact]
    public void Not_retry_a_failure_reported_after_the_character_left_the_instance()
    {
        _w.Parties.Leave(_c.Id);
        _w.Clock.Advance(TimeSpan.FromSeconds(60));
        Assert.Single(_w.Parties.Tick());
        _c.Character.InstanceId = Guid.NewGuid();

        _w.Parties.ReturnFailed(_c.Id, new InvalidOperationException("build failed"));

        Assert.False(_w.Parties.InCountdown(_c.Id));
        Assert.Contains(_log.Errors, e => e.Exception is InvalidOperationException);
    }

    /// <summary>A respawn already under way when the countdown runs out is not doubled: the countdown is checked again after the delay.</summary>
    [Fact]
    public void Check_again_later_when_a_return_is_already_under_way()
    {
        _w.Parties.Leave(_c.Id);
        _c.Connection.RespawnInFlight = true;

        After(60);

        Assert.Equal(0, _lookups);
        Assert.True(_c.Connection.RespawnInFlight);
        Assert.True(_w.Parties.InCountdown(_c.Id));

        _c.Connection.RespawnInFlight = false; // that respawn failed and left the character here
        After(5);

        Assert.Equal(1, _lookups);
        Assert.Equal(_townInstance.InstanceId, _c.Character.InstanceId);
    }
}
