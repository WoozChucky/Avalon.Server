using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

/// <summary>Why an item use was or was not applied (item use, 2026-10-02). Append-only.</summary>
public enum ItemUseResult : byte
{
    /// <summary>What a payload without the field decodes as. Never sent.</summary>
    Unknown = 0,

    /// <summary>Equipped, or the script ran. The change itself arrives in the tick's SInventoryUpdatePacket.</summary>
    Ok = 1,

    /// <summary>The slot is empty, out of range, or not in the Bag.</summary>
    NotFound = 2,

    /// <summary>The item cannot be worn and names no script, its template is gone, or it is a stack of gear.</summary>
    NotUsable = 3,

    Dead = 4,

    /// <summary>The item's cooldown or its group's is running; CooldownMs is the time left.</summary>
    OnCooldown = 5,

    /// <summary>An ability cast is in progress.</summary>
    AlreadyCasting = 6,

    /// <summary>Reserved: nothing sends it yet.</summary>
    InCombat = 7,

    /// <summary>Reserved: nothing sends it yet.</summary>
    NotInCombat = 8,

    WrongEquipSlot = 9,
    LevelTooLow = 10,
    WrongClass = 11,

    /// <summary>A two-hander's off-hand item has no free Bag slot to go to.</summary>
    TargetFull = 12,

    /// <summary>A two-handed weapon and an off-hand item would both be worn.</summary>
    Blocked = 13,

    /// <summary>The cast ended early: the character moved, died, left, used another item or cast an ability, or the item left its slot.</summary>
    Interrupted = 14,

    /// <summary>The item's script refused; Message is its line.</summary>
    Refused = 15,

    /// <summary>Something on the server failed; it is logged. Nothing was consumed.</summary>
    InternalError = 16,
}

/// <summary>The one answer to a CItemUsePacket, sent to the requester only.</summary>
[ProtoContract]
public class SItemUseResultPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_ITEM_USE_RESULT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    /// <summary>The request's RequestId, echoed back.</summary>
    [ProtoMember(1)] public uint RequestId { get; set; }

    [ProtoMember(2)] public ItemUseResult Result { get; set; }

    /// <summary>On OnCooldown, the milliseconds left, rounded up so it is at least 1; 0 otherwise.</summary>
    [ProtoMember(3)] public uint CooldownMs { get; set; }

    /// <summary>On Refused, the line to show the player; absent otherwise.</summary>
    [ProtoMember(4)] public string? Message { get; set; }

    public static NetworkPacket Create(uint requestId, ItemUseResult result, uint cooldownMs, string? message,
        EncryptFunc encrypt)
        => PacketSerializationHelper.Serialize(
            new SItemUseResultPacket { RequestId = requestId, Result = result, CooldownMs = cooldownMs, Message = message },
            PacketType, Flags, Protocol, encrypt);
}
