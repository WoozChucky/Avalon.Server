using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Loot;

/// <summary>
/// Drops entering the client's view (#1269), whoever owns them: a kill's or a doodad's within the interest radius, those
/// in view when the client enters the instance, and those coming into view as the client or its view moves. It may name a
/// drop the client was told of before and let go of since; the client keeps one of each guid.
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

    public static OutboundPacket Create(List<LootDropDto> drops, PacketEncoder encoder)
    {
        SLootSpawnedPacket message = PacketEncoder.Scratch<SLootSpawnedPacket>();
        message.Drops = drops;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}
