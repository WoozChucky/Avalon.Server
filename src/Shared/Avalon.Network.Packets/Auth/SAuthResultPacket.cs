using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class SAuthResultPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_AUTH_RESULT;
    private const NetworkProtocol Protocol = NetworkProtocol.Tcp;
    [ProtoMember(1)] public string AccountId { get; set; }
    [ProtoMember(2)] public bool Success { get; set; }
    [ProtoMember(3)] public string Message { get; set; }
    [ProtoMember(4)] public byte[] PrivateKey { get; set; }
    
    public static NetworkPacket Create(string accountId, bool success, string message, byte[] privateKey)
    {
        using var memoryStream = new MemoryStream();
        
        var byePacket = new SAuthResultPacket()
        {
            AccountId = accountId,
            Success = success,
            Message = message,
            PrivateKey = privateKey
        };
        
        Serializer.Serialize(memoryStream, byePacket);
        
        return new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type = PacketType,
                Flags = NetworkPacketFlags.None,
                Protocol = Protocol,
                Version = 0
            },
            Payload = memoryStream.ToArray()
        };
    }
}
