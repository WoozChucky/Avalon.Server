using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Combat;

[ProtoContract]
public class SCreatureAttackAnimationPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CREATURE_ATTACK_ANIMATION;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;
    
    [ProtoMember(1)] public Guid Attacker { get; set; }
    [ProtoMember(2)] public ushort AnimationId { get; set; }
    
    public static NetworkPacket Create(Guid attacker, ushort animationId, Func<byte[], byte[]> encryptFunc)
    {
        using var memoryStream = new MemoryStream();
        
        var p = new SCreatureAttackAnimationPacket
        {
            Attacker = attacker,
            AnimationId = animationId
        };
        
        Serializer.Serialize(memoryStream, p);
        
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
