using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Social;

[ProtoContract]
public class CChatMessagePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_CHAT_MESSAGE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;
    
    [ProtoMember(1)] public int AccountId { get; set; }
    [ProtoMember(2)] public int PlayerId { get; set; }
    [ProtoMember(3)] public string Message { get; set; }
    [ProtoMember(4)] public DateTime DateTime { get; set; }

    public static NetworkPacket Create(int accountId, int playerId, string message, DateTime dateTime, Func<byte[], byte[]> encryptFunc)
    {
        using var memoryStream = new MemoryStream();
        
        var movementPacket = new CChatMessagePacket()
        {
            AccountId = accountId,
            PlayerId = playerId,
            Message = message,
            DateTime = dateTime
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
