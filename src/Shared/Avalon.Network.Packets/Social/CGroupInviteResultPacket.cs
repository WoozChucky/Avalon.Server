using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Social;

[ProtoContract]
public class CGroupInviteResultPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_GROUP_INVITE_RESULT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    
    [ProtoMember(1)] public int AccountId { get; set; }
    [ProtoMember(2)] public int CharacterId { get; set; }
    [ProtoMember(3)] public int InviterAccountId { get; set; }
    [ProtoMember(4)] public int InviterCharacterId { get; set; }
    [ProtoMember(5)] public bool Accepted { get; set; }

    public static NetworkPacket Create(int accountId, int characterId, int inviterAccountId, int inviterCharacterId, bool accepted)
    {
        using var memoryStream = new MemoryStream();
        
        var movementPacket = new CGroupInviteResultPacket()
        {
            AccountId = accountId,
            CharacterId = characterId,
            InviterAccountId = inviterAccountId,
            InviterCharacterId = inviterCharacterId,
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
