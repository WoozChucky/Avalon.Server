using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Social;

[ProtoContract]
public class SGroupInvitePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_GROUP_INVITE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;
    
    [ProtoMember(1)] public int AccountId { get; set; }
    [ProtoMember(2)] public int InviterAccountId { get; set; }
    [ProtoMember(3)] public int InviterCharacterId { get; set; }
    [ProtoMember(4)] public string InviterName { get; set; }

    public static NetworkPacket Create(int accountId, int inviterAccountId, int inviterCharacterId, string inviterName, Func<byte[], byte[]> encryptFunc)
    {
        using var memoryStream = new MemoryStream();
        
        var movementPacket = new SGroupInvitePacket()
        {
            AccountId = accountId,
            InviterAccountId = inviterAccountId,
            InviterCharacterId = inviterCharacterId,
            InviterName = inviterName
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
