using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using Avalon.Network.Packets.World;
using ProtoBuf;

namespace Avalon.Network.Packets.Abilities;

[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_CAST_ABILITY)]
public class CCastAbilityPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_CAST_ABILITY;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public uint AbilityId { get; set; }
    /// <summary>Deprecated and ignored by the server (#164); kept so older clients still decode. Casts aim, they do not target.</summary>
    [ProtoMember(2)] public ulong? TargetGuid { get; set; }

    /// <summary>The cursor's ground point, required by a Cursor skill (else NoAimPoint). Its height is ignored.</summary>
    [ProtoMember(3)] public Vector3Dto? GroundPos { get; set; }

    /// <summary>
    /// The world tick the client was drawing other units at when the player cast (#1082): its render clock turned back
    /// into a tick through the time-sync ping's anchor (<c>SPingPacket.WorldTick</c>). The server judges the cast's hits
    /// against where units stood on that tick, clamped to at most 15 ticks (250 ms) behind its own. 0 when not sent:
    /// no rewind, the hits are judged at the present, as from a client before #1082.
    /// </summary>
    [ProtoMember(4)] public long ViewTick { get; set; }
}
