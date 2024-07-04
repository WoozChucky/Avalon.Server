using System;
using System.IO;
using ProtoBuf;

namespace Avalon.Network.Packets.Abstractions;

[ProtoContract]
public class NetworkPacket
{
    [ProtoMember(1)] public NetworkPacketHeader Header { get; set; } = new();
    [ProtoMember(2)] public byte[] Payload { get; set; } = [];

    public int Size => Header?.Size + Payload?.Length ?? 0;
    
    public static NetworkPacket Deserialize(ReadOnlyMemory<byte> buffer)
    {
        return Serializer.Deserialize<NetworkPacket>(buffer);
    }
}

[ProtoContract]
public class NetworkPacketHeader
{
    [ProtoMember(1)] public NetworkPacketType Type { get; set; }
    [ProtoMember(2)] public NetworkPacketFlags Flags { get; set; }
    [ProtoMember(3)] public NetworkProtocol Protocol { get; set; }
    [ProtoMember(4)] public int Version { get; set; }
    
    public int Size => 2 + 2 + 2 + 4;
    
    public static NetworkPacketHeader Deserialize(ReadOnlyMemory<byte> buffer)
    {
        return Serializer.Deserialize<NetworkPacketHeader>(buffer);
    }
}
