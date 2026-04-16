using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Social;

[ProtoContract]
public class SChatMessagePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CHAT_MESSAGE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public ulong AccountId { get; set; }
    [ProtoMember(2)] public ulong CharacterId { get; set; }
    [ProtoMember(3)] public string CharacterName { get; set; }
    [ProtoMember(4)] public string Message { get; set; }
    [ProtoMember(5)] public DateTime DateTime { get; set; }

    public static NetworkPacket Create(ulong accountId, ulong characterId, string characterName, string message, DateTime dateTime, EncryptFunc encryptFunc)
        => PacketSerializationHelper.Serialize(
            new SChatMessagePacket { AccountId = accountId, CharacterId = characterId, CharacterName = characterName, Message = message, DateTime = dateTime },
            PacketType, Flags, Protocol, encryptFunc);
}
