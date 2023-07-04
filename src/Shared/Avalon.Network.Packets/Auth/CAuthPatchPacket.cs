using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class CAuthPatchPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_AUTH_PATCH;
    public static NetworkProtocol Protocol = NetworkProtocol.Udp;
    
    [ProtoMember(1)] public int AccountId { get; set; }
    [ProtoMember(2)] public byte[] PrivateKey { get; set; }

    public static NetworkPacket Create(int accountId, byte[] privateKey)
    {
        using var memoryStream = new MemoryStream();
        
        var authPacket = new CAuthPatchPacket()
        {
            AccountId = accountId,
            PrivateKey = privateKey
        };
        
        Serializer.Serialize(memoryStream, authPacket);
        
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
