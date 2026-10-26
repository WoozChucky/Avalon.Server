using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

/// <summary>
/// Client to server (#1180): at a Resonance Workbench, destroy the identified magic or rare item in one Bag slot and keep
/// one of its awake affixes as a glyph, which takes the item's slot. Nothing is opened on the server first: every request
/// is checked afresh, the Workbench's range included. It costs no gold. Answered with exactly one
/// SItemExtractResultPacket.
/// </summary>
[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_ITEM_EXTRACT)]
public class CItemExtractPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_ITEM_EXTRACT;
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

    /// <summary>
    /// The catalog modifier of the affix to keep, as the item's modifier list named it. One the item does not carry, or
    /// that is not an awake prefix or suffix, is refused as NotExtractable.
    /// </summary>
    [ProtoMember(4)] public uint ModifierId { get; set; }
}
