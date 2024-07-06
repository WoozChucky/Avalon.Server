using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_CHARACTER_SELECTED)]
public class CCharacterSelectedPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_CHARACTER_SELECTED;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;
    
    [ProtoMember(1)] public ulong CharacterId { get; set; }

    public static NetworkPacket Create(ulong characterId, Func<byte[], byte[]> encrypt)
    {
        using var memoryStream = new MemoryStream();
        
        var p = new CCharacterSelectedPacket()
        {
            CharacterId = characterId
        };
        
        Serializer.Serialize(memoryStream, p);
        
        var buffer = encrypt(memoryStream.ToArray());
        
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
