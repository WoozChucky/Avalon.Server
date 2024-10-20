using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Social;

[ProtoContract]
public class CGroupInviteResultPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_GROUP_INVITE_RESULT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public ulong AccountId { get; set; }
    [ProtoMember(2)] public ulong CharacterId { get; set; }
    [ProtoMember(3)] public ulong InviterAccountId { get; set; }
    [ProtoMember(4)] public ulong InviterCharacterId { get; set; }
    [ProtoMember(5)] public bool Accepted { get; set; }

    public static NetworkPacket Create(ulong accountId, ulong characterId, ulong inviterAccountId, ulong inviterCharacterId, bool accepted, Func<byte[], byte[]> encryptFunc)
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
                Flags = Flags,
                Protocol = Protocol,
                Version = 0
            },
            Payload = memoryStream.ToArray()
        };
    }
}
