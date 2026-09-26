using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

/// <summary>
/// Client to server: destroy some or all of the stack in one slot. An item whose template is
/// NoDestroy is refused. Answered with exactly one SItemResultPacket.
/// </summary>
[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_ITEM_DESTROY)]
public class CItemDestroyPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_ITEM_DESTROY;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    /// <summary>Chosen by the client and echoed in the result.</summary>
    [ProtoMember(1)] public uint RequestId { get; set; }

    /// <summary>InventoryType number: 0 Equipment, 1 Bag, 2 Bank.</summary>
    [ProtoMember(2)] public uint Container { get; set; }

    [ProtoMember(3)] public uint Slot { get; set; }

    /// <summary>How many to destroy. Absent means the whole stack; 0 is refused as InvalidCount.</summary>
    [ProtoMember(4)] public uint? Count { get; set; }
}
