namespace Avalon.Network.Packets.Combat;

using Abstractions;
using Abstractions.Attributes;
using ProtoBuf;

[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_ATTACK)]
public class CCharacterAttackPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_ATTACK;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;
    
    [ProtoMember(1)] public Guid Target { get; set; }
    [ProtoMember(2)] public bool AutoAttack { get; set; }
    [ProtoMember(3)] public uint? SpellId { get; set; }

    public static NetworkPacket Create(Guid target, bool autoAttack, uint? spellId, Func<byte[], byte[]> encryptFunc)
    {
        using var memoryStream = new MemoryStream();
        
        var p = new CCharacterAttackPacket
        {
            Target = target,
            AutoAttack = autoAttack,
            SpellId = spellId
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
