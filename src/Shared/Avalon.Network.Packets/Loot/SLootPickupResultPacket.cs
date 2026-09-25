using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Loot;

/// <summary>Why a pickup did or did not happen. Anything but Ok leaves the drop where it was.</summary>
public enum LootPickupResult : byte
{
    Ok = 0,

    /// <summary>No such drop in the picker's instance: already taken, or never there.</summary>
    NotFound = 1,

    /// <summary>Further away than GameConfiguration.LootPickupRange.</summary>
    TooFar = 2,

    /// <summary>Reserved for another character until its FreeForAllAt.</summary>
    NotYours = 3,

    InventoryFull = 4,

    /// <summary>A unique item the picker already has one of.</summary>
    UniqueAlreadyOwned = 5,

    /// <summary>The pile would take the picker's balance past GameConfiguration.MaxMoney.</summary>
    MoneyCapReached = 6,
}

/// <summary>The answer to one CLootPickupPacket, sent to the picker only.</summary>
[ProtoContract]
public class SLootPickupResultPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_LOOT_PICKUP_RESULT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    /// <summary>The drop the request named, echoed back.</summary>
    [ProtoMember(1)] public ulong LootGuid { get; set; }

    [ProtoMember(2)] public LootPickupResult Result { get; set; }

    public static NetworkPacket Create(ulong lootGuid, LootPickupResult result, EncryptFunc encrypt)
        => PacketSerializationHelper.Serialize(
            new SLootPickupResultPacket { LootGuid = lootGuid, Result = result },
            PacketType, Flags, Protocol, encrypt);
}
