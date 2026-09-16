using Avalon.Database.Character.Repositories;
using Avalon.Database.World.Repositories;
using Avalon.Network.Packets.Character;
using Avalon.Server.World.UnitTests.Characters;
using Avalon.World;
using Avalon.World.Handlers;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Instances;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Handlers;

/// <summary>
/// The list, create and delete handlers all refuse a connection that has already selected. A
/// character waiting on the readiness barrier has selected: it is built, and its row is already
/// marked online. Before the barrier that state lasted as long as a few database round trips; it
/// now lasts as long as a client takes to load a map.
/// </summary>
public class SelectPhaseHandlersShould
{
    private static IWorldConnection PendingConnection() => PendingSpawnConnection.Create(
        new PendingSpawn(PendingSpawnConnection.Character(), Substitute.For<IMapInstance>(),
            DateTime.UtcNow.Ticks));

    [Fact]
    public void Refuse_a_character_list_request_while_a_spawn_is_pending()
    {
        var characters = Substitute.For<ICharacterRepository>();
        var handler = new CharacterListHandler(
            NullLogger<CharacterListHandler>.Instance, characters, Substitute.For<IWorld>());
        IWorldConnection connection = PendingConnection();

        handler.Execute(connection, new CCharacterListPacket());

        connection.Received().Close(Arg.Any<bool>());
        characters.DidNotReceiveWithAnyArgs().FindByAccountAsync(default!, default);
    }

    [Fact]
    public void Refuse_a_character_create_while_a_spawn_is_pending()
    {
        var characters = Substitute.For<ICharacterRepository>();
        var handler = new CharacterCreateHandler(
            NullLogger<CharacterCreateHandler>.Instance,
            characters,
            Substitute.For<ICharacterStatsRepository>(),
            Substitute.For<ICharacterAbilityRepository>(),
            Substitute.For<ICharacterInventoryRepository>(),
            Substitute.For<IItemInstanceRepository>(),
            Substitute.For<IWorld>());
        IWorldConnection connection = PendingConnection();

        handler.Execute(connection, new CCharacterCreatePacket());

        connection.Received().Close(Arg.Any<bool>());
        characters.DidNotReceiveWithAnyArgs().FindByAccountAsync(default!, default);
    }

    /// <summary>
    /// The one with teeth: the character it would delete is the one being spawned.
    /// </summary>
    [Fact]
    public void Refuse_a_character_delete_while_a_spawn_is_pending()
    {
        var characters = Substitute.For<ICharacterRepository>();
        var handler = new CharacterDeletetHandler(
            NullLogger<CharacterDeletetHandler>.Instance, characters);
        IWorldConnection connection = PendingConnection();

        handler.Execute(connection, new CCharacterDeletePacket());

        connection.Received().Close(Arg.Any<bool>());
        characters.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);
    }
}
