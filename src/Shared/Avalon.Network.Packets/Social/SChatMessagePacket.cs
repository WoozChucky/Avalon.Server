using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

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

    /// <summary>
    /// The sending character's class (#763), as <c>CharacterClass</c>, so the client can colour the name in
    /// <see cref="CharacterName" />: set on said, party and whispered lines, the whisper's echo included, since that
    /// name is the sender's there too. 0 on a line no character sent (a System line, a script's whisper), and a
    /// payload from before it existed reads as 0.
    /// </summary>
    [ProtoMember(8)] public ushort CharacterClass { get; set; }

    public static OutboundPacket Create(ulong accountId, ulong characterId, string characterName, string message,
        DateTime dateTime, PacketEncoder encoder, ChatChannel channel = ChatChannel.Say, string? targetName = null,
        ushort characterClass = 0)
    {
        SChatMessagePacket packet = PacketEncoder.Scratch<SChatMessagePacket>();
        packet.AccountId = accountId;
        packet.CharacterId = characterId;
        packet.CharacterName = characterName;
        packet.Message = message;
        packet.DateTime = dateTime;
        packet.Channel = channel;
        packet.TargetName = targetName;
        packet.CharacterClass = characterClass;
        return encoder.Encode(packet, PacketType, Flags, Protocol);
    }

    /// <summary>A line from the server, on the system channel.</summary>
    public static OutboundPacket System(string message, DateTime dateTime, PacketEncoder encoder)
        => Create(0UL, 0UL, "System", message, dateTime, encoder, ChatChannel.System);
}
