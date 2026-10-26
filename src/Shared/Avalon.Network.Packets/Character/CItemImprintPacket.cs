using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

/// <summary>
/// Client to server (#1180): at a Resonance Workbench, imprint the glyph in one Bag slot onto a free affix slot of the
/// identified magic or rare item in another, for gold by the glyph's tier. The glyph is spent, and the item is bound.
/// Nothing is opened on the server first: every request is checked afresh, the Workbench's range included. Answered with
/// exactly one SItemImprintResultPacket.
/// </summary>
[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_ITEM_IMPRINT)]
public class CItemImprintPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_ITEM_IMPRINT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    /// <summary>Chosen by the client and echoed in the result, so it can pair answers with requests.</summary>
    [ProtoMember(1)] public uint RequestId { get; set; }

    /// <summary>The Workbench's guid, as the world state named it.</summary>
    [ProtoMember(2)] public ulong WorkbenchGuid { get; set; }

    /// <summary>
    /// The Bag slot of the glyph. A byte, as no container has 255 slots: a number past 255 fails the packet's decode, and
    /// one the Bag does not have, or one holding no glyph, is refused as NoGlyph.
    /// </summary>
    [ProtoMember(3)] public byte GlyphBagSlot { get; set; }

    /// <summary>
    /// The Bag slot of the item to imprint. One the Bag does not have, or an empty one, is refused as NoItem; the glyph's
    /// own slot is refused as WrongRarity, as any glyph is.
    /// </summary>
    [ProtoMember(4)] public byte TargetBagSlot { get; set; }
}
