using Avalon.Common;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Loot;

/// <summary>
/// Drops the client is to let go of (#1269): picked up, sent to every client holding one of them, or out of the client's
/// view. It may name a drop the client no longer holds, which the client ignores; by itself it is no pickup.
/// </summary>
[ProtoContract]
public class SLootDespawnedPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_LOOT_DESPAWNED;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public List<ulong> LootGuids { get; set; } = [];

    public static OutboundPacket Create(IReadOnlyList<ObjectGuid> lootGuids, PacketEncoder encoder)
    {
        // Read before the scratch is taken: nothing a caller's list runs can reach the message (#875). By index, in
        // the caller's order: a query here cost an iterator per pickup (#883).
        var ids = new List<ulong>(lootGuids.Count);
        for (int i = 0; i < lootGuids.Count; i++)
            ids.Add(lootGuids[i].RawValue);
        SLootDespawnedPacket message = PacketEncoder.Scratch<SLootDespawnedPacket>();
        message.LootGuids = ids;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }

    /// <summary>
    /// The raw guids as they are, encoded from the caller's list (#1269): a list the instance keeps and refills, so a
    /// despawn allocates no list of its own. Encoded before this returns, so the caller may refill the list after.
    /// </summary>
    public static OutboundPacket Create(List<ulong> lootGuids, PacketEncoder encoder)
    {
        SLootDespawnedPacket message = PacketEncoder.Scratch<SLootDespawnedPacket>();
        message.LootGuids = lootGuids;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}
