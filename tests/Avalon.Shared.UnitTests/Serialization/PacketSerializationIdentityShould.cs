using System.Buffers;
using System.Reflection;
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
/// sets, must encode (<see cref="PacketEncoder" />, #875) to the very bytes the buffer writer wrote.
/// </summary>
public class PacketSerializationIdentityShould
{
    private static readonly MethodInfo s_encode = typeof(PacketEncoder).GetMethod(nameof(PacketEncoder.Encode))!;

    private static readonly PacketEncoder s_encoder = new(new PayloadSegmentPool());

    /// <summary>protobuf-net's buffer-writer entry point, which the helper called before #640.</summary>
    private static readonly MethodInfo s_toBufferWriter = typeof(Serializer)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(m => m.Name == nameof(Serializer.Serialize) && m.IsGenericMethodDefinition
                     && m.GetParameters() is [{ ParameterType: var destination }, _, { ParameterType: var state }]
                     && destination == typeof(IBufferWriter<byte>) && state == typeof(object));

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
        Encode(typeof(SChunkLayoutPacket), WireFixtures.Build(typeof(SChunkLayoutPacket), FixtureVariant.Maxima)).Release();

        OutboundPacket encoded = Encode(packetType, packet);

        Assert.Equal(expected, encoded.PayloadMemory.ToArray());
        encoded.Release();
    }

    private static OutboundPacket Encode(Type packetType, object packet) =>
        (OutboundPacket)s_encode.MakeGenericMethod(packetType).Invoke(s_encoder,
            [packet, NetworkPacketType.SMSG_PING, NetworkPacketFlags.Encrypted, NetworkProtocol.Tcp])!;
}
