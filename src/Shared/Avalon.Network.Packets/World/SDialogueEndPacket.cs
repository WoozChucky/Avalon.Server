using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.World;

/// <summary>
/// Server→Client: the conversation is over. Carries the speaker so a client can match the close to
/// the window it opened.
/// </summary>
[ProtoContract]
public class SDialogueEndPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_DIALOGUE_END;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public ulong SpeakerGuid { get; set; }

    public static OutboundPacket Create(ulong speakerGuid, PacketEncoder encoder)
    {
        SDialogueEndPacket message = PacketEncoder.Scratch<SDialogueEndPacket>();
        message.SpeakerGuid = speakerGuid;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}
