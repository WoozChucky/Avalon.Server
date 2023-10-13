using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Social;

[ProtoContract]
public class SCloseChatPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CHAT_CLOSE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;
    
    [ProtoMember(1)] public string ClientId { get; set; }

    public static NetworkPacket Create(string clientId, Func<byte[], byte[]> encryptFunc)
    {
        using var memoryStream = new MemoryStream();
        
        var movementPacket = new SCloseChatPacket()
        {
            ClientId = clientId
        };
        
        Serializer.Serialize(memoryStream, movementPacket);
        
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
