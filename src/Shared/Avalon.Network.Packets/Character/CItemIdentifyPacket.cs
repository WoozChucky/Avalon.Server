using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

/// <summary>
/// Client to server (#1179): identify the item in one Bag slot at a Resonance Workbench, a doodad whose catalog service
/// is Identify. Nothing is opened on the server first: the client opens its window when the player clicks the Workbench,
/// and every request is checked afresh, the Workbench's range included. One item per request; identifying several is one
/// request each. Answered with exactly one SItemIdentifyResultPacket.
/// </summary>
[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_ITEM_IDENTIFY)]
public class CItemIdentifyPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_ITEM_IDENTIFY;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    /// <summary>Chosen by the client and echoed in the result, so it can pair answers with requests.</summary>
    [ProtoMember(1)] public uint RequestId { get; set; }

    /// <summary>The Workbench's guid, as the world state named it.</summary>
    [ProtoMember(2)] public ulong WorkbenchGuid { get; set; }

    /// <summary>
    /// The Bag slot of the item. A byte, as no container has 255 slots: a number past 255 fails the packet's decode, and
    /// one the Bag does not have is refused as NoItem.
    /// </summary>
    [ProtoMember(3)] public byte BagSlot { get; set; }
}
