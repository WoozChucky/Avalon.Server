using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

[ProtoContract]
public class CCharacterListPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_CHARACTER_LIST;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;
    
    [ProtoMember(1)] public int AccountId { get; set; }

    public static NetworkPacket Create(int accountId, Func<byte[], byte[]> encrypt)
    {
        using var memoryStream = new MemoryStream();
        
        var authPacket = new CCharacterListPacket()
        {
            AccountId = accountId,
        };
        
        Serializer.Serialize(memoryStream, authPacket);
        
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
