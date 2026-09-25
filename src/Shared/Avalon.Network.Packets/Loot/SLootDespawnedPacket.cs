using Avalon.Common;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Loot;

/// <summary>Drops that left the ground because someone picked them up. Sent to everyone in the instance.</summary>
[ProtoContract]
public class SLootDespawnedPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_LOOT_DESPAWNED;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public List<ulong> LootGuids { get; set; } = [];

    public static NetworkPacket Create(IEnumerable<ObjectGuid> lootGuids, EncryptFunc encrypt)
        => PacketSerializationHelper.Serialize(
            new SLootDespawnedPacket { LootGuids = lootGuids.Select(g => g.RawValue).ToList() },
            PacketType, Flags, Protocol, encrypt);
}
