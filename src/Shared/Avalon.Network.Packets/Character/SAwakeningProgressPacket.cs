using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

/// <summary>
/// The bars of the sealed affixes a character wears (#1180), to its owner only, at most once a tick and only on a tick
/// their progress moved: every worn sealed affix, in Equipment slot order, with the points it holds. An affix that woke
/// is no longer sealed, so it is not listed. Its threshold is the one the item's modifier entry carries.
/// </summary>
[ProtoContract]
public class SAwakeningProgressPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_AWAKENING_PROGRESS;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public List<AwakeningProgressDto> Items { get; set; } = [];

    /// <summary>Encodes <paramref name="items" /> now: the caller may reuse the list and its entries once this returns.</summary>
    public static OutboundPacket Create(List<AwakeningProgressDto> items, PacketEncoder encoder)
    {
        SAwakeningProgressPacket packet = PacketEncoder.Scratch<SAwakeningProgressPacket>();
        packet.Items = items;
        return encoder.Encode(packet, PacketType, Flags, Protocol);
    }
}

/// <summary>
/// One worn sealed affix's bar (#1180): the Equipment slot of its item, the points it holds, and the item's instance id
/// (the one its <see cref="ItemSlotDto" /> carries), so a client applies the bar to that item even when this tick's
/// inventory update, which follows, moves it to another slot.
/// </summary>
[ProtoContract]
public class AwakeningProgressDto
{
    [ProtoMember(1)] public uint EquipmentSlot { get; set; }

    [ProtoMember(2)] public uint Progress { get; set; }

    [ProtoMember(3)] public Guid ItemInstanceId { get; set; }
}
