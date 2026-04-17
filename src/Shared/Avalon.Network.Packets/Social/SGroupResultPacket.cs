using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Social;

[ProtoContract]
public class SGroupResultPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_GROUP_INVITE_RESULT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public int AccountId { get; set; }
    [ProtoMember(2)] public int CharacterId { get; set; }
    [ProtoMember(3)] public int InviterAccountId { get; set; }
    [ProtoMember(4)] public int InviterCharacterId { get; set; }
    [ProtoMember(5)] public bool Accepted { get; set; }

    public static NetworkPacket Create(int accountId, int characterId, int inviterAccountId, int inviterCharacterId, bool accepted, EncryptFunc encryptFunc)
        => PacketSerializationHelper.Serialize(
            new SGroupResultPacket { AccountId = accountId, CharacterId = characterId, InviterAccountId = inviterAccountId, InviterCharacterId = inviterCharacterId, Accepted = accepted },
            PacketType, Flags, Protocol, encryptFunc);
}
