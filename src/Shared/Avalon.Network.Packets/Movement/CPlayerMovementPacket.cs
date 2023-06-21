using ProtoBuf;

namespace Avalon.Network.Packets.Movement;

[ProtoContract]
public class CPlayerMovementPacket
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_MOVEMENT;
    
    [ProtoMember(1)] public float ElapsedGameTime { get; set; }
    [ProtoMember(2)] public float X { get; set; }
    [ProtoMember(3)] public float Y { get; set; }
}
