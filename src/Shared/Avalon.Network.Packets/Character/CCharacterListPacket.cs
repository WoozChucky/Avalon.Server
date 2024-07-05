using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_CHARACTER_LIST)]
public class CCharacterListPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_CHARACTER_LIST;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    public static NetworkPacket Create(Func<byte[], byte[]> encrypt)
    {
        using var memoryStream = new MemoryStream();
        
        var p = new CCharacterListPacket();
        
        Serializer.Serialize(memoryStream, p);
        
        var encrypted = encrypt(memoryStream.ToArray());
        
        return new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type = PacketType,
                Flags = Flags,
                Protocol = Protocol,
                Version = 0
            },
            Payload = encrypted
        };
    }
}
