using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Combat;

/// <summary>
/// Asks to toggle the PvP flag (#164): off turns on at once; on with no timer starts the off timer; on
/// with a timer running cancels it. Answered with SPvpStatePacket. The same as typing /pvp.
/// </summary>
[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_PVP_TOGGLE)]
public class CPvpTogglePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_PVP_TOGGLE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;
}
