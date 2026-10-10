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

    /// <summary>
    /// Raw frames as a peer could send them. Expected header as { type, flags, protocol, version }, or null when the
    /// frame must be refused.
    /// </summary>
    public static TheoryData<string, byte[], int[]?> RawFrames() => new()
    {
        {
            "out of order, with unknown field 5 as varint and as length-delimited",
            [
                0x12, 0x01, 0xAA,             // payload before the header
                0x28, 0x01,                   // field 5, varint
                0x2A, 0x01, 0x00,             // field 5, length-delimited
                0x0A, 0x10,                   // header, 16 bytes:
                0x20, 0x07,                   //   version 7
                0x18, 0x01,                   //   protocol Tcp
                0x28, 0x96, 0x01,             //   field 5, varint
                0x10, 0x04,                   //   flags Encrypted
                0x2A, 0x02, 0xFF, 0xFF,       //   field 5, length-delimited
                0x08, 0x86, 0x40,             //   type CMSG_PONG (0x2006)
            ],
            [(int)NetworkPacketType.CMSG_PONG, (int)NetworkPacketFlags.Encrypted, (int)NetworkProtocol.Tcp, 7]
        },
        // The header claims two bytes; its type varint goes on past them.
        { "truncated varint", [0x0A, 0x02, 0x08, 0x86, 0x40], null },
        { "eleven-byte varint", [0x0A, 0x0C, 0x08, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01], null },
        { "header length past the end", [0x0A, 0x05, 0x08, 0x01], null },
        { "wire type 3", [0x0B, 0x00], null },
        { "type above short.MaxValue", [0x0A, 0x04, 0x08, 0x80, 0x80, 0x02], null },
        { "version above int.MaxValue", [0x0A, 0x06, 0x20, 0x80, 0x80, 0x80, 0x80, 0x10], null },
        { "unknown length-delimited field longer than what remains", [0x2A, 0x05, 0x00], null },
        { "fixed64 field cut short", [0x29, 0x00, 0x00], null },
    };

    /// <summary>
    /// <see cref="InboundPacketFrame.ParseFrame"/> is a hand-written parser of what a peer sends (#875). It reads a
    /// header in any field order past fields it does not know, and refuses what protobuf-net refused, with
    /// <see cref="InvalidDataException"/> rather than by reading past the frame.
    /// </summary>
    [Theory]
    [MemberData(nameof(RawFrames))]
    public void Read_or_refuse_a_raw_frame(string because, byte[] raw, int[]? expected)
    {
        if (expected is null)
        {
            Assert.Throws<InvalidDataException>(() => InboundPacketFrame.ParseFrame(raw));
            return;
        }

        var frame = InboundPacketFrame.ParseFrame(raw);

        Assert.True(
            expected.SequenceEqual([(int)frame.Header.Type, (int)frame.Header.Flags, (int)frame.Header.Protocol, frame.Header.Version]),
            because);
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
