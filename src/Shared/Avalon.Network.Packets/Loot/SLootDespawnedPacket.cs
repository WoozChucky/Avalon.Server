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
}
