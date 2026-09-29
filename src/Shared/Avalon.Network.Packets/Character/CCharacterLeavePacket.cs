using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

/// <summary>
/// Client to server: leave the character in the world and return to character selection, keeping
/// the world connection (#663). Answered with exactly one SCharacterLeaveResultPacket, unless the
/// connection is closed instead. After <see cref="CharacterLeaveResult.Left" /> the connection holds
/// no character and may send CMSG_CHARACTER_LIST and select again.
/// </summary>
[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_CHARACTER_LEAVE)]
public class CCharacterLeavePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_CHARACTER_LEAVE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;
}
