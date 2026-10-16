using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

/// <summary>
/// Client to server, no payload (#967): the player quits or returns to the launcher on purpose. Sent before the client
/// closes; the world ends the session as a logout and releases the account's place on the world at once. A close without
/// it is a drop, and the place is held for a short while. Never sent for Change Character, which keeps the session.
/// </summary>
[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_LEAVE_WORLD)]
public class CLeaveWorldPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_LEAVE_WORLD;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;
}
