using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

/// <summary>Why an item request was or was not applied. Anything but Ok changed nothing.</summary>
public enum ItemRequestResult : byte
{
    Ok = 0,

    /// <summary>
    /// The source slot is empty. An item whose template no longer exists is still found: it can be
    /// moved within the Bag and the Bank, taken off, and destroyed, and only equipping it is refused,
    /// with <see cref="WrongEquipSlot" />.
    /// </summary>
    NotFound = 1,

    /// <summary>A container or slot out of range, the same slot twice, or a reserved equipment slot.</summary>
    InvalidSlot = 2,

    /// <summary>A count of 0 or above the stack, or part of a stack aimed at a different item.</summary>
    InvalidCount = 3,

    /// <summary>The item has no equipment slot, or not the one it was aimed at.</summary>
    WrongEquipSlot = 4,

    /// <summary>The character is below the item's required level.</summary>
    LevelTooLow = 5,

    /// <summary>The character's class is not one the item allows.</summary>
    WrongClass = 6,

    /// <summary>A split or merge of an item that does not stack, or any split or merge into Equipment.</summary>
    NotStackable = 7,

    /// <summary>A merge onto a stack already at its maximum.</summary>
    TargetFull = 8,

    /// <summary>The item cannot be destroyed.</summary>
    CannotDestroy = 9,

    /// <summary>The request touches the Bank and no bank is open within reach.</summary>
    BankClosed = 10,

    /// <summary>The character is dead.</summary>
    Dead = 11,

    /// <summary>A two-handed weapon and the off hand would both be held.</summary>
    Blocked = 12,
}

/// <summary>
/// The answer to one CItemMovePacket or CItemDestroyPacket, sent to the requester only, for every
/// request, accepted or refused.
/// </summary>
[ProtoContract]
public class SItemResultPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_ITEM_RESULT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    /// <summary>The request's RequestId, echoed back.</summary>
    [ProtoMember(1)] public uint RequestId { get; set; }

    [ProtoMember(2)] public ItemRequestResult Result { get; set; }

    /// <summary>
    /// Empty on Ok, because an accepted change arrives in the tick's SInventoryUpdatePacket. On a
    /// refusal, the current absolute state of every slot the request named that the server could
    /// read, so a client that moved an icon early can put it back. A Bank slot is left out while
    /// the bank is closed.
    /// </summary>
    [ProtoMember(3)] public InventorySlotUpdateDto[] Slots { get; set; } = [];

    public static NetworkPacket Create(uint requestId, ItemRequestResult result, InventorySlotUpdateDto[] slots,
        EncryptFunc encrypt)
        => PacketSerializationHelper.Serialize(
            new SItemResultPacket { RequestId = requestId, Result = result, Slots = slots },
            PacketType, Flags, Protocol, encrypt);
}
