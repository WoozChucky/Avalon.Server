using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

/// <summary>
/// Client to server: take what is in one slot to another. The server decides from what the two
/// slots hold and from the count whether that is a move, a split, a merge or a swap, and whether an
/// equip is allowed; the client names no action and no item id. Answered with exactly one
/// SItemResultPacket. An accepted change also arrives in the tick's SInventoryUpdatePacket.
/// </summary>
/// <remarks>
/// Containers cross as InventoryType numbers: 0 Equipment, 1 Bag, 2 Bank. Equipment slots are
/// 0 Head, 1 Neck, 2 Shoulder, 3 Chest, 4 Hands, 5 Legs, 6 Feet, 7 and 8 Finger, 9 MainHand,
/// 10 OffHand; 11-13 are reserved and always refused. A Bank slot is only usable while a
/// banker conversation that opened the bank is still open.
/// </remarks>
[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_ITEM_MOVE)]
public class CItemMovePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_ITEM_MOVE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    /// <summary>Chosen by the client and echoed in the result, so it can pair answers with requests.</summary>
    [ProtoMember(1)] public uint RequestId { get; set; }

    /// <summary>InventoryType number of the slot the item leaves.</summary>
    [ProtoMember(2)] public uint FromContainer { get; set; }

    [ProtoMember(3)] public uint FromSlot { get; set; }

    /// <summary>InventoryType number of the slot the item goes to.</summary>
    [ProtoMember(4)] public uint ToContainer { get; set; }

    [ProtoMember(5)] public uint ToSlot { get; set; }

    /// <summary>How many to take. Absent means the whole stack; 0 is refused as InvalidCount.</summary>
    [ProtoMember(6)] public uint? Count { get; set; }
}
