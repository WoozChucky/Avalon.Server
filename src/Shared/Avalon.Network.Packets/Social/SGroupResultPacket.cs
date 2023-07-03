using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Social;

[ProtoContract]
public class SGroupResultPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_GROUP_INVITE_RESULT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    
    [ProtoMember(1)] public string ClientId { get; set; }
    [ProtoMember(2)] public string GroupClientId { get; set; }
    [ProtoMember(3)] public bool Accepted { get; set; }

    public static NetworkPacket Create(string clientId, string groupClientId, bool accepted)
    {
        using var memoryStream = new MemoryStream();
        
        var movementPacket = new SGroupResultPacket()
        {
            ClientId = clientId,
            GroupClientId = groupClientId,
            Accepted = accepted
        };
        
        Serializer.Serialize(memoryStream, movementPacket);
        
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
