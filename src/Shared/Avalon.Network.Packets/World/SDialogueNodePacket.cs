using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.World;

[ProtoContract]
public class SDialogueOptionInfo
{
    [ProtoMember(1)] public int OptionId { get; set; }
    [ProtoMember(2)] public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Server→Client: one node of a conversation. All text is already localised and interpolated — the
/// client substitutes nothing and knows nothing about tokens.
/// </summary>
[ProtoContract]
public class SDialogueNodePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_DIALOGUE_NODE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public ulong SpeakerGuid { get; set; }
    [ProtoMember(2)] public string SpeakerName { get; set; } = string.Empty;
    [ProtoMember(3)] public int NodeId { get; set; }
    [ProtoMember(4)] public string Text { get; set; } = string.Empty;
    [ProtoMember(5)] public List<SDialogueOptionInfo> Options { get; set; } = [];

    public static NetworkPacket Create(
        ulong speakerGuid,
        string speakerName,
        int nodeId,
        string text,
        List<SDialogueOptionInfo> options,
        EncryptFunc encryptFunc)
        => PacketSerializationHelper.Serialize(
            new SDialogueNodePacket
            {
                SpeakerGuid = speakerGuid,
                SpeakerName = speakerName,
                NodeId = nodeId,
                Text = text,
                Options = options
            },
            PacketType, Flags, Protocol, encryptFunc);
}
