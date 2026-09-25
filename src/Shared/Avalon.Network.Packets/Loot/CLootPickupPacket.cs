using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Loot;

/// <summary>
/// Client to server: the player clicked a drop. Always answered with SLootPickupResultPacket while
/// the character is alive. There is no pickup-all.
/// </summary>
[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_LOOT_PICKUP)]
public class CLootPickupPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_LOOT_PICKUP;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    /// <summary>Raw ObjectGuid of the drop, as SLootSpawnedPacket gave it.</summary>
    [ProtoMember(1)] public ulong LootGuid { get; set; }
}
