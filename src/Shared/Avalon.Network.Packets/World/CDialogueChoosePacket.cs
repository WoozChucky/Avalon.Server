using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.World;

/// <summary>
/// Client→Server: the player picked an option. <see cref="NodeId"/> is redundant with server state
/// by design — it is what lets the server reject a choice made against a node it is no longer
/// showing, which is the whole reason the conversation is server-authoritative.
/// </summary>
[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_DIALOGUE_CHOOSE)]
public class CDialogueChoosePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_DIALOGUE_CHOOSE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public ulong TargetGuid { get; set; }
    [ProtoMember(2)] public int NodeId { get; set; }
    [ProtoMember(3)] public int OptionId { get; set; }
}
