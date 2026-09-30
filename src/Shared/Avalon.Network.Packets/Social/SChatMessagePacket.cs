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

    [ProtoMember(6)] public ChatChannel Channel { get; set; }

    /// <summary>
    /// Set only on the echo of a whisper the recipient's connection sent (#717): the name of the character it went to,
    /// so the client can show "To Kaela: ...". Absent on every other line, the recipient's whisper included, so a line
    /// with it is always the sender's own; a payload from before it existed reads as null.
    /// </summary>
    [ProtoMember(7)] public string? TargetName { get; set; }

    public static NetworkPacket Create(ulong accountId, ulong characterId, string characterName, string message,
        DateTime dateTime, EncryptFunc encryptFunc, ChatChannel channel = ChatChannel.Say, string? targetName = null)
        => PacketSerializationHelper.Serialize(
            new SChatMessagePacket
            {
                AccountId = accountId, CharacterId = characterId, CharacterName = characterName, Message = message,
                DateTime = dateTime, Channel = channel, TargetName = targetName
            },
            PacketType, Flags, Protocol, encryptFunc);

    /// <summary>A line from the server, on the system channel.</summary>
    public static NetworkPacket System(string message, DateTime dateTime, EncryptFunc encryptFunc)
        => Create(0UL, 0UL, "System", message, dateTime, encryptFunc, ChatChannel.System);
}
