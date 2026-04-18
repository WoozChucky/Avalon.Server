using System.IO;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using Xunit;

namespace Avalon.Shared.UnitTests.Networking;

public class InboundPacketFrameShould
{
    [Fact]
    public void ParseHeaderFields_FromSerializedNetworkPacket()
    {
        var expected = new NetworkPacketHeader
        {
            Type = NetworkPacketType.CMSG_PING,
            Flags = NetworkPacketFlags.None,
            Protocol = NetworkProtocol.Tcp,
            Version = 1
        };
        var packet = new NetworkPacket { Header = expected, Payload = [0x01, 0x02, 0x03] };
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, packet);

        var frame = InboundPacketFrame.ParseFrame(ms.ToArray().AsMemory());

        Assert.Equal(expected.Type, frame.Header.Type);
        Assert.Equal(expected.Flags, frame.Header.Flags);
        Assert.Equal(expected.Protocol, frame.Header.Protocol);
        Assert.Equal(expected.Version, frame.Header.Version);
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
