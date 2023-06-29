using ProtoBuf;

namespace Avalon.Network.Packets.Abstractions;

[ProtoContract]
public class NetworkPacket
{
    [ProtoMember(1)] public NetworkPacketHeader Header { get; set; }
    [ProtoMember(2)] public byte[] Payload { get; set; }

    public int Size => Header.Size + Payload.Length;
}

[ProtoContract]
public class NetworkPacketHeader
{
    [ProtoMember(1)] public NetworkPacketType Type { get; set; }
    [ProtoMember(2)] public NetworkPacketFlags Flags { get; set; }
    [ProtoMember(3)] public NetworkProtocol Protocol { get; set; }
    [ProtoMember(4)] public NetworkChannel Channel { get; set; }
    [ProtoMember(5)] public int Version { get; set; }
    
    public int Size => 1 + 1 + 1 + 1 + 4;
}
