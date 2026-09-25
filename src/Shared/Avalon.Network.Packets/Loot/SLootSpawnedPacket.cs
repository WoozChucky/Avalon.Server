using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Loot;

/// <summary>
/// Drops that appeared on the ground: every drop of one kill, sent to everyone in the instance, or
/// every drop already there, sent to a character entering it. Every player sees every drop,
/// whoever owns it.
/// </summary>
/// <remarks>
/// Two things the client has to handle itself:
/// <list type="bullet">
/// <item>Clear every drop it knows of on SMapTransitionPacket and on SCharacterSelectedPacket.
/// Leaving an instance sends no SLootDespawnedPacket for the drops left behind in it.</item>
/// <item>For a kill, this packet arrives before that creature's SUnitDeathPacket, in the same
/// flush: the drops are rolled the moment the creature dies, and the combat code broadcasts the
/// death after that.</item>
/// </list>
/// </remarks>
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
