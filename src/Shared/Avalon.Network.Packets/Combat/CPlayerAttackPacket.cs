namespace Avalon.Network.Packets.Combat;

using Abstractions;
using Abstractions.Attributes;
using ProtoBuf;

[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_ATTACK)]
public class CPlayerAttackPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_ATTACK;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;
    
    [ProtoMember(1)] public Guid Target { get; set; }
    [ProtoMember(2)] public uint Damage { get; set; }

    public static NetworkPacket Create(Guid target, uint damage, Func<byte[], byte[]> encryptFunc)
    {
        using var memoryStream = new MemoryStream();
        
        var p = new CPlayerAttackPacket
        {
            Target = target,
            Damage = damage
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
