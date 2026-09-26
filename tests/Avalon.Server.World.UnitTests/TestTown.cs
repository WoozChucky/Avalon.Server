using System.IO;
using Avalon.Common;
using Avalon.Common.Accounts;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abstractions;
using Avalon.World;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using NSubstitute;
using ProtoBuf;

namespace Avalon.Server.World.UnitTests;

/// <summary>
/// The pieces the town fixtures (BankerWorld, VendorWorld) share: a substituted world whose
/// registry knows only the given instances, substituted NPCs, and connections that record what
/// they are sent.
/// </summary>
internal static class TestTown
{
    /// <summary>Wires <paramref name="world" /> to <paramref name="data" /> and a registry holding only <paramref name="instances" />.</summary>
    public static void Stub(IWorld world, StaticData data, params (Guid Id, IMapInstance Instance)[] instances)
    {
        var registry = Substitute.For<IInstanceRegistry>();
        registry.GetInstanceById(Arg.Any<Guid>()).Returns((IMapInstance?)null);
        foreach ((Guid id, IMapInstance instance) in instances)
            registry.GetInstanceById(id).Returns(instance);

        world.InstanceRegistry.Returns(registry);
        world.Configuration.Returns(new GameConfiguration());
        world.Data.Returns(data);
    }

    /// <summary>A live NPC of <paramref name="template" /> at <paramref name="at" />, added to <paramref name="creatures" />.</summary>
    public static ICreature AddNpc(
        Dictionary<ObjectGuid, ICreature> creatures, ObjectGuid guid, CreatureTemplateId template, string name, Vector3 at)
    {
        var metadata = Substitute.For<ICreatureMetadata>();
        metadata.Id.Returns(template);
        var npc = Substitute.For<ICreature>();
        npc.Guid.Returns(guid);
        npc.Name.Returns(name);
        npc.CurrentHealth.Returns(100u);
        npc.Position.Returns(at);
        npc.Metadata.Returns(metadata);
        creatures[guid] = npc;
        return npc;
    }

    /// <summary>Makes <paramref name="connection" /> hold <paramref name="character" /> and record every packet into <paramref name="sent" />.</summary>
    public static void Record(IWorldConnection connection, CharacterEntity character, List<NetworkPacket> sent)
    {
        connection.Character.Returns(character);
        connection.Locale.Returns(AccountLocale.enUS);
        connection.CryptoSession.Returns(new FakeAvalonCryptoSession());
        connection.When(c => c.Send(Arg.Any<NetworkPacket>())).Do(ci => sent.Add(ci.Arg<NetworkPacket>()));
    }

    /// <summary>Every recorded packet of <paramref name="type" />, deserialized.</summary>
    public static List<T> Read<T>(IEnumerable<NetworkPacket> sent, NetworkPacketType type) =>
        sent.Where(p => p.Header.Type == type).Select(p =>
        {
            using var stream = new MemoryStream(p.Payload);
            return Serializer.Deserialize<T>(stream);
        }).ToList();
}
