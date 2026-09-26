using System.IO;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.State;
using Avalon.World;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using NSubstitute;
using ProtoBuf;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>A real character in a real MapInstance, and every packet its connection was sent.</summary>
internal sealed record MapInstanceClient(IWorldConnection Connection, CharacterEntity Character, List<NetworkPacket> Sent)
{
    /// <summary>Every packet of <paramref name="type" /> this client was sent, decoded, oldest first.</summary>
    public List<T> Read<T>(NetworkPacketType type) => Sent
        .Where(p => p.Header.Type == type)
        .Select(p =>
        {
            using var stream = new MemoryStream(p.Payload);
            return Serializer.Deserialize<T>(stream);
        })
        .ToList();

    /// <summary>Every object state this client was sent in a world state update, oldest first.</summary>
    public List<ObjectState> StateUpdates() => Read<SInstanceStateUpdatePacket>(NetworkPacketType.SMSG_WORLD_STATE_UPDATE)
        .SelectMany(p => p.Updates ?? [])
        .ToList();
}

/// <summary>
/// The shared setup for tests that drive a real MapInstance: a world with no map templates, and a
/// character joined through a connection that records what it is sent. Character ids must be unique
/// per test class: some character events are static, so parallel classes would otherwise mix.
/// </summary>
internal static class MapInstanceClients
{
    /// <summary>A world with default game configuration, no map templates, and whatever data is given.</summary>
    public static IWorld NewWorld(StaticData? data = null)
    {
        var world = Substitute.For<IWorld>();
        world.Configuration.Returns(new GameConfiguration());
        world.MapTemplates.Returns(new List<MapTemplate>());
        if (data is not null)
        {
            world.Data.Returns(data);
        }

        return world;
    }

    /// <summary>A new character <paramref name="id" /> with no abilities, added to the instance.</summary>
    public static MapInstanceClient Join(MapInstance instance, uint id)
    {
        CharacterEntity character = Inventory.TestCharacters.New(id);
        character.Spells.Load(Array.Empty<IAbility>());   // the tick updates abilities; an unloaded list throws
        character.InstanceId = instance.InstanceId;         // a cast looks its instance up by this

        var sent = new List<NetworkPacket>();
        IWorldConnection connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns(character);
        connection.CryptoSession.Returns(new FakeAvalonCryptoSession());
        connection.When(c => c.Send(Arg.Any<NetworkPacket>())).Do(ci => sent.Add(ci.Arg<NetworkPacket>()));

        instance.AddCharacter(connection);
        return new MapInstanceClient(connection, character, sent);
    }
}
