using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Combat;

[ProtoContract]
public class SCreatureDamagePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CREATURE_DAMAGED;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;
    
    [ProtoMember(1)] public ulong Attacker { get; set; }
    [ProtoMember(2)] public ulong Target { get; set; }
    [ProtoMember(3)] public uint CurrentHealth { get; set; }
    [ProtoMember(4)] public uint Damage { get; set; }
    
    public static NetworkPacket Create(ulong attacker, ulong target, uint currentHealth, uint damage, Func<byte[], byte[]> encryptFunc)
    {
        using var memoryStream = new MemoryStream();
        
        var p = new SCreatureDamagePacket
        {
            Attacker = attacker,
            Target = target,
            CurrentHealth = currentHealth,
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
