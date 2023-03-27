using ProtoBuf;

namespace Avalon.Network.Packets;

public enum PacketType
{
    LoginPacket = 1,
    DetailPacket = 2
}

[ProtoContract]
public class UserPacket
{
    [ProtoMember(1)] public PacketType Type { get; set; }
    [ProtoMember(2)] public byte[] Content { get; set; }
    
}

[ProtoContract]
public class UserDetails
{
    [ProtoMember(1)] public int Id { get; set; }
    [ProtoMember(2)] public bool Active { get; set; }
}
