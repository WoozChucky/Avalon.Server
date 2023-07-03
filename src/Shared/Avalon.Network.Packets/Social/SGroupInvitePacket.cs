using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Social;

[ProtoContract]
public class SGroupInvitePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_GROUP_INVITE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    
    [ProtoMember(1)] public string ClientId { get; set; }
    [ProtoMember(2)] public string InvitedById { get; set; }

    public static NetworkPacket Create(string clientId, string invitedClientId)
    {
        using var memoryStream = new MemoryStream();
        
        var movementPacket = new SGroupInvitePacket()
        {
            ClientId = clientId,
            InvitedById = invitedClientId
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
