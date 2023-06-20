using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class CAuthPacket
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_AUTH;
    [ProtoMember(1)] public string Username { get; set; }
    [ProtoMember(2)] public string Password { get; set; }
}
