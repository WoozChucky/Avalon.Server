using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Loot;

/// <summary>
/// Drops that appeared on the ground: every drop of one kill, sent to everyone in the instance, or
/// every drop already there, sent to a character entering it. Every player sees every drop,
/// whoever owns it.
/// </summary>
[ProtoContract]
public class SLootSpawnedPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_LOOT_SPAWNED;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public List<LootDropDto> Drops { get; set; } = [];

    public static NetworkPacket Create(List<LootDropDto> drops, EncryptFunc encrypt)
        => PacketSerializationHelper.Serialize(
            new SLootSpawnedPacket { Drops = drops },
            PacketType, Flags, Protocol, encrypt);
}
