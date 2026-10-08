using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

/// <summary>
/// Client to server (item use): use the item in one slot. A Bag item (Container 1) that is gear is equipped into the
/// slot its type is worn in, swapping out what is there; any other Bag item runs its script, at once or after a cast bar.
/// A worn item (Container 0, an Equipment slot) is taken off to the lowest free Bag slot (TargetFull when the Bag is
/// full). Answered with exactly one SItemUseResultPacket, at once, or when a cast ends (the cast's SMSG_UNIT_START_CAST
/// is the acknowledgement meanwhile). Any other container is answered NotFound.
/// </summary>
[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_ITEM_USE)]
public class CItemUsePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_ITEM_USE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    /// <summary>Chosen by the client and echoed in the result, so it can pair answers with requests.</summary>
    [ProtoMember(1)] public uint RequestId { get; set; }

    /// <summary>InventoryType number of the slot: 1, the Bag, or 0, Equipment.</summary>
    [ProtoMember(2)] public uint Container { get; set; }

    [ProtoMember(3)] public uint Slot { get; set; }
}
