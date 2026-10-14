using Avalon.Common;
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

    /// <summary>The container of the slot. A number that is no container is refused as InvalidSlot.</summary>
    [ProtoMember(2)] public InventoryType Container { get; set; }

    /// <summary>
    /// The slot in that container. A byte, as no container has 255 slots (#921): a number past 255 fails the
    /// packet's decode; one the container does not have is refused as InvalidSlot.
    /// </summary>
    [ProtoMember(3)] public byte Slot { get; set; }

    /// <summary>How many to destroy. Absent means the whole stack; 0 is refused as InvalidCount.</summary>
    [ProtoMember(4)] public uint? Count { get; set; }
}
