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
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Respawn;

public class TownReturnShould
{
    private readonly IWorldConnection _connection = Substitute.For<IWorldConnection>();
    private readonly ICharacter _character = Substitute.For<ICharacter>();
    private readonly IWorld _world = Substitute.For<IWorld>();
    private readonly IMapInstance _source = Substitute.For<IMapInstance>();
    private readonly IMapInstance _townInstance = Substitute.For<IMapInstance>();
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
        _connection.When(c => c.EnqueueContinuation(Arg.Any<Task<MapTemplateId>>(), Arg.Any<Action<MapTemplateId>>()))
            .Do(ci => ci.Arg<Action<MapTemplateId>>()(ci.Arg<Task<MapTemplateId>>().Result));
        _connection.When(c => c.EnqueueContinuation(Arg.Any<Task<IMapInstance>>(), Arg.Any<Action<IMapInstance>>()))
            .Do(ci => ci.Arg<Action<IMapInstance>>()(ci.Arg<Task<IMapInstance>>().Result));

        ICombatService combat = Substitute.For<ICombatService>();
        _source.CombatService.Returns(combat);
        _source.InstanceId.Returns(sourceId);
        _townInstance.InstanceId.Returns(Guid.NewGuid());

        var registry = Substitute.For<IInstanceRegistry>();
        registry.GetInstanceById(sourceId).Returns(_source);
        registry.GetOrCreateTownInstanceAsync(Arg.Any<MapTemplateId>(), Arg.Any<ushort>())
            .Returns(Task.FromResult(_townInstance));

        _world.InstanceRegistry.Returns(registry);
        _world.MapTemplates.Returns(new List<MapTemplate>
        {
            new() { Id = new MapTemplateId(1), MapType = MapType.Town, Name = "town", Description = "" },
        });

        var resolver = Substitute.For<IRespawnTargetResolver>();
        resolver.ResolveTownAsync(Arg.Any<MapTemplateId>(), Arg.Any<CancellationToken>())
            .Returns(new MapTemplateId(1));

        _town = new TownReturn(NullLogger.Instance, _world, resolver, Substitute.For<IChunkLibrary>());
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
}
