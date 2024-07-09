using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Movement;

[ProtoContract]
public class SPlayerPositionUpdatePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_PLAYER_POSITION_UPDATE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;
    
    [ProtoMember(1)] public SPlayerPacket[] Players { get; set; }
    
    public static NetworkPacket Create(SPlayerPacket[] players, Func<byte[], byte[]> encryptFunc)
    {
        using var memoryStream = new MemoryStream();
        
        var packet = new SPlayerPositionUpdatePacket()
        {
            Players = players
        };
        
        Serializer.Serialize(memoryStream, packet);
        
        var buffer = encryptFunc(memoryStream.ToArray());
        
        return new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type = PacketType,
                Flags = Flags,
                Protocol = Protocol,
                Version = 0
            },
            Payload = buffer
        };
    }
}

[ProtoContract]
public class SPlayerPacket
{
    [ProtoMember(1)] public ulong AccountId { get; set; }
    [ProtoMember(2)] public ulong CharacterId { get; set; }
    [ProtoMember(3)] public float PositionX { get; set; }
    [ProtoMember(4)] public float PositionY { get; set; }
    [ProtoMember(5)] public float PositionZ { get; set; }
    [ProtoMember(6)] public float VelocityX { get; set; }
    [ProtoMember(7)] public float VelocityY { get; set; }
    [ProtoMember(8)] public float VelocityZ { get; set; }
    [ProtoMember(9)] public bool Chatting { get; set; }
    [ProtoMember(10)] public float Elapsed { get; set; }
}
