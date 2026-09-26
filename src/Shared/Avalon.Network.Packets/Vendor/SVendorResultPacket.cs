using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Vendor;

/// <summary>Why a vendor request was or was not applied. Anything but Ok changed nothing.</summary>
public enum VendorResult : byte
{
    Ok = 0,

    /// <summary>No open vendor conversation within 15 m of the vendor.</summary>
    ShopClosed = 1,

    /// <summary>An unknown or quest-gated row, an empty or out-of-range Bag slot, or an unknown buyback index.</summary>
    NotFound = 2,

    /// <summary>The row has fewer left than the count asked for.</summary>
    OutOfStock = 3,

    /// <summary>A count of 0, above the stack, or above the item's MaxStackSize.</summary>
    InvalidCount = 4,

    NotEnoughGold = 5,

    /// <summary>The Bag lacks a required cost item times the count.</summary>
    MissingItems = 6,

    InventoryFull = 7,

    UniqueAlreadyOwned = 8,

    /// <summary>The item is NoSell, sells for nothing, or its template no longer exists.</summary>
    NotSellable = 9,

    /// <summary>The sale would take the balance past the configured maximum.</summary>
    MoneyCapReached = 10,

    Dead = 11,

    /// <summary>The item's durability is below full, so it cannot be sold.</summary>
    Damaged = 12,
}

/// <summary>The answer to one vendor request, sent to the requester only, for every request.</summary>
[ProtoContract]
public class SVendorResultPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_VENDOR_RESULT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    /// <summary>The request's RequestId, echoed back.</summary>
    [ProtoMember(1)] public uint RequestId { get; set; }

    [ProtoMember(2)] public VendorResult Result { get; set; }

    public static NetworkPacket Create(uint requestId, VendorResult result, EncryptFunc encrypt)
        => PacketSerializationHelper.Serialize(
            new SVendorResultPacket { RequestId = requestId, Result = result },
            PacketType, Flags, Protocol, encrypt);
}
