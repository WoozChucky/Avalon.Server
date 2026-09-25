using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.World;

/// <summary>
/// Client→Server: the player wants to talk to a unit. The server replies with
/// <c>SDialogueNodePacket</c>, or with nothing at all when the target has no dialogue — which is
/// the ordinary case for every monster in the game.
/// </summary>
[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_INTERACT)]
public class CInteractPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_INTERACT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    /// <summary>Raw <c>ObjectGuid</c> of the unit to interact with.</summary>
    [ProtoMember(1)] public ulong TargetGuid { get; set; }
}
