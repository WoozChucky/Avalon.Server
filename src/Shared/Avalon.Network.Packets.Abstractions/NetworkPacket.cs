using ProtoBuf;

namespace Avalon.Network.Packets.Abstractions;

[ProtoContract]
public class NetworkPacket
{
    // The wire contract stays as it is: IsRequired would change what is serialized (WireSchemaShould pins it).
    [ProtoMember(1)] public NetworkPacketHeader Header { get; set; }
    [ProtoMember(2)] public byte[] Payload { get; set; } = [];

    public int Size => Header.Size + (Payload?.Length ?? 0);

    [Obsolete("Server inbound path uses InboundPacketFrame.ParseFrame. This method is retained for client-side compatibility only.")]
    public static NetworkPacket Deserialize(ReadOnlyMemory<byte> buffer)
    {
        return Serializer.Deserialize<NetworkPacket>(buffer);
    }
}

/// <summary>
/// A packet's header. A value (#875): the read loop reads one per frame received and the send path writes one per
/// frame sent, and as a class each cost an object. On the wire it is the same message as before.
/// </summary>
[ProtoContract]
public struct NetworkPacketHeader
{
    [ProtoMember(1)] public NetworkPacketType Type { get; set; }
    [ProtoMember(2)] public NetworkPacketFlags Flags { get; set; }
    [ProtoMember(3)] public NetworkProtocol Protocol { get; set; }
    [ProtoMember(4)] public int Version { get; set; }

    /// <summary>
    /// What the byte counters add to a packet's payload (<c>network.in.bytes</c>, <c>network.out.bytes</c> and each
    /// connection's byte rates): a nominal 10 bytes, whatever the header holds. It is not the header's length on the
    /// wire, and the counters are approximate because of it.
    /// </summary>
    /// <remarks>
    /// A frame puts 10 to 16 bytes on the wire around its payload: the header itself, 5 bytes plain and 7 sealed or
    /// flagged ClearText (a field that is 0 is not written); the frame's length prefix, 1 to 3 bytes; and the tag and
    /// length in front of the header and of the payload, 4 to 6 bytes. So 10 is exact for a plain packet whose frame
    /// is under 128 bytes, and up to 6 bytes short for a sealed or a longer one. TLS's own records are counted
    /// nowhere. What is counted is left as it is: dashboards read these counters, and changing it would move every
    /// series. <c>network.send.bytes</c> is the exact figure for what a world hands its sockets.
    /// </remarks>
    public readonly int Size => 2 + 2 + 2 + 4;

    public static NetworkPacketHeader Deserialize(ReadOnlyMemory<byte> buffer)
    {
        return Serializer.Deserialize<NetworkPacketHeader>(buffer);
    }
}
