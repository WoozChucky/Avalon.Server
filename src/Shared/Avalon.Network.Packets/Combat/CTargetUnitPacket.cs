using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using Avalon.Network.Packets.World;
using ProtoBuf;
using NetworkPacketFlags = Avalon.Network.Packets.Abstractions.NetworkPacketFlags;
using NetworkProtocol = Avalon.Network.Packets.Abstractions.NetworkProtocol;

namespace Avalon.Network.Packets.Combat;

/// <summary>
/// Client→Server: signals the player's current target selection (or clears it when null).
/// The world server stores this on <c>IWorldConnection.CurrentTargetGuid</c> and uses it to
/// determine which encounter's threat list to broadcast back via <c>SThreatListPacket</c>.
/// </summary>
[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_TARGET_UNIT)]
public class CTargetUnitPacket : Packet
{
    public static NetworkPacketType  PacketType = NetworkPacketType.CMSG_TARGET_UNIT;
    public static NetworkProtocol    Protocol   = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags      = NetworkPacketFlags.Encrypted;

    /// <summary>
    /// Raw <c>ObjectGuid</c> of the targeted unit, or <c>null</c> to clear the current target.
    /// </summary>
    [ProtoMember(1)] public ulong? TargetGuid { get; set; }
}
