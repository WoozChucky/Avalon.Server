using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Social;

/// <summary>
/// The recipient's whole ignore list (#723), oldest entry first. Sent when the character enters the world and after
/// every change to the list; no entries means the list is empty. A client replaces what it holds with it.
/// </summary>
[ProtoContract]
public class SIgnoreListPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_IGNORE_LIST;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public List<IgnoredCharacterDto> Characters { get; set; } = [];

    public static OutboundPacket Create(List<IgnoredCharacterDto> characters, PacketEncoder encoder)
    {
        SIgnoreListPacket message = PacketEncoder.Scratch<SIgnoreListPacket>();
        message.Characters = characters;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}
