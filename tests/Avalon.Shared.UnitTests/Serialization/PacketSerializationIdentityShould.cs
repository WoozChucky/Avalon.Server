using System.Buffers;
using System.Reflection;
using Avalon.Common.Cryptography;
using Avalon.Exporter;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Handshake;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.State;
using Avalon.Network.Packets.Vendor;
using Avalon.Network.Packets.World;
using ProtoBuf;
using Xunit;

namespace Avalon.Shared.UnitTests.Serialization;

/// <summary>
/// #640: packets are serialized through a reused stream rather than a buffer writer, for every packet
/// the server sends. Every server-to-client packet, filled with each of the wire corpus's sample value
/// sets, must serialize to the very bytes the buffer writer wrote, encrypted and unencrypted alike.
/// </summary>
public class PacketSerializationIdentityShould
{
    private static readonly MethodInfo s_encrypted = typeof(Packet).Assembly
        .GetType("Avalon.Network.Packets.Serialization.PacketSerializationHelper", throwOnError: true)!
        .GetMethod("Serialize", BindingFlags.Public | BindingFlags.Static)!;

    private static readonly MethodInfo s_unencrypted = s_encrypted.DeclaringType!
        .GetMethod("SerializeUnencrypted", BindingFlags.Public | BindingFlags.Static)!;

    /// <summary>protobuf-net's buffer-writer entry point, which the helper called before #640.</summary>
    private static readonly MethodInfo s_toBufferWriter = typeof(Serializer)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(m => m.Name == nameof(Serializer.Serialize) && m.IsGenericMethodDefinition
                     && m.GetParameters() is [{ ParameterType: var destination }, _, { ParameterType: var state }]
                     && destination == typeof(IBufferWriter<byte>) && state == typeof(object));

    private static readonly EncryptFunc s_identity = static span => span.ToArray();

    /// <summary>Every [ProtoContract] packet the server sends: a Packet named S*.</summary>
    public static IReadOnlyList<Type> ServerPackets() => WireSchema.ContractTypes()
        .Where(type => type.IsSubclassOf(typeof(Packet)) && type.Name.StartsWith('S'))
        .ToList();

    public static TheoryData<string, string> Cases()
    {
        var cases = new TheoryData<string, string>();
        foreach (Type packet in ServerPackets())
        {
            foreach (FixtureVariant variant in WireFixtures.VariantsFor(packet))
                cases.Add(packet.FullName!, variant.ToString());
        }

        return cases;
    }

    [Fact]
    public void Cover_the_packets_whose_shapes_differ_most()
    {
        IReadOnlyList<Type> packets = ServerPackets();

        Assert.Contains(typeof(SInstanceStateRemovePacket), packets);
        Assert.Contains(typeof(SInstanceStateUpdatePacket), packets);
        Assert.Contains(typeof(SChunkLayoutPacket), packets);
        Assert.Contains(typeof(SVendorListPacket), packets);
        Assert.Contains(typeof(SInventorySnapshotPacket), packets);
        Assert.Contains(typeof(SServerInfoPacket), packets);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Write_every_server_packet_as_the_buffer_writer_did(string packetName, string variantName)
    {
        Type packetType = typeof(Packet).Assembly.GetType(packetName, throwOnError: true)!;
        FixtureVariant variant = Enum.Parse<FixtureVariant>(variantName);
        object packet = WireFixtures.Build(packetType, variant);

        using var reference = new PooledArrayBufferWriter();
        s_toBufferWriter.MakeGenericMethod(packetType).Invoke(null, [reference, packet, null]);
        byte[] expected = reference.WrittenSpan.ToArray();

        // After a larger packet, so the reused stream must start each one afresh.
        Serialize(s_encrypted, typeof(SChunkLayoutPacket),
            WireFixtures.Build(typeof(SChunkLayoutPacket), FixtureVariant.Maxima), s_identity);

        NetworkPacket encrypted = Serialize(s_encrypted, packetType, packet, s_identity);
        NetworkPacket unencrypted = Serialize(s_unencrypted, packetType, packet, encrypt: null);

        Assert.Equal(expected, encrypted.Payload);
        Assert.Equal(expected, unencrypted.Payload);
    }

    private static NetworkPacket Serialize(MethodInfo entry, Type packetType, object packet, EncryptFunc? encrypt)
    {
        object?[] args = encrypt is null
            ? [packet, NetworkPacketType.SMSG_PING, NetworkPacketFlags.None, NetworkProtocol.Tcp]
            : [packet, NetworkPacketType.SMSG_PING, NetworkPacketFlags.Encrypted, NetworkProtocol.Tcp, encrypt];
        return (NetworkPacket)entry.MakeGenericMethod(packetType).Invoke(null, args)!;
    }
}
