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

    public readonly int Size => 2 + 2 + 2 + 4;

    public static NetworkPacketHeader Deserialize(ReadOnlyMemory<byte> buffer)
    {
        return Serializer.Deserialize<NetworkPacketHeader>(buffer);
    }
}
