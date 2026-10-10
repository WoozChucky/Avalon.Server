using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using Xunit;

namespace Avalon.Shared.UnitTests.Networking;

public class InboundPacketFrameShould
{
    public static TheoryData<NetworkPacketType, NetworkPacketFlags, NetworkProtocol, int> Headers() => new()
    {
        { NetworkPacketType.CMSG_PING, NetworkPacketFlags.None, NetworkProtocol.Tcp, 1 },
        { NetworkPacketType.CMSG_PLAYER_INPUT, NetworkPacketFlags.Encrypted, NetworkProtocol.Tcp, 0 },
        // A negative type is a ten-byte varint on the wire: protobuf-net writes an int32 sign-extended.
        { NetworkPacketType.ERROR, NetworkPacketFlags.ClearText | NetworkPacketFlags.Handshake, NetworkProtocol.Both, int.MaxValue },
        // Every field 0: protobuf-net writes none of them, and the header is an empty message.
        { NetworkPacketType.UNKNOWN, NetworkPacketFlags.None, NetworkProtocol.Invalid, 0 },
        { NetworkPacketType.CMSG_PONG, NetworkPacketFlags.None, NetworkProtocol.Udp, -1 },
    };

    /// <summary>
    /// The read loop reads every frame's header by hand (#875): protobuf-net allocates for a header it deserializes,
    /// once per packet received. What it reads must be what protobuf-net wrote, field by field.
    /// </summary>
    [Theory]
    [MemberData(nameof(Headers))]
    public void Read_the_header_protobuf_net_wrote(NetworkPacketType type, NetworkPacketFlags flags,
        NetworkProtocol protocol, int version)
    {
        var expected = new NetworkPacketHeader { Type = type, Flags = flags, Protocol = protocol, Version = version };
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, new NetworkPacket { Header = expected, Payload = [0x01, 0x02, 0x03] });

        var frame = InboundPacketFrame.ParseFrame(ms.ToArray().AsMemory());

        Assert.Equal(expected, frame.Header);
        Assert.Equal([0x01, 0x02, 0x03], frame.Payload.ToArray());
    }

    [Fact]
    public void ReturnPayloadSliceMatchingOriginalBytes()
    {
        byte[] payloadBytes = [0x0A, 0x1B, 0x2C, 0x3D];
        var packet = new NetworkPacket
        {
            Header = new NetworkPacketHeader { Type = NetworkPacketType.CMSG_PING },
            Payload = payloadBytes
        };
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, packet);

        var frame = InboundPacketFrame.ParseFrame(ms.ToArray().AsMemory());

        Assert.Equal(payloadBytes, frame.Payload.ToArray());
    }

    [Fact]
    public void ReturnEmptyPayload_WhenPayloadFieldAbsent()
    {
        var packet = new NetworkPacket
        {
            Header = new NetworkPacketHeader { Type = NetworkPacketType.CMSG_PING },
            Payload = []
        };
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, packet);

        var frame = InboundPacketFrame.ParseFrame(ms.ToArray().AsMemory());

        Assert.True(frame.Payload.IsEmpty);
    }
}
