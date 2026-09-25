using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

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

    public static NetworkPacket Create(ulong speakerGuid, EncryptFunc encryptFunc)
        => PacketSerializationHelper.Serialize(
            new SDialogueEndPacket { SpeakerGuid = speakerGuid },
            PacketType, Flags, Protocol, encryptFunc);
}
