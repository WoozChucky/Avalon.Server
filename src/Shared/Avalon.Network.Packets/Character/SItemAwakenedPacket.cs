using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Items;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

/// <summary>
/// A worn item's sealed affix woke (#1180), to its owner only, once, on the tick its bar was found full. The item itself,
/// now attuned and showing the woken modifier, arrives in the same tick's SInventoryUpdatePacket.
/// </summary>
[ProtoContract]
public class SItemAwakenedPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_ITEM_AWAKENED;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    /// <summary>The Equipment slot of the item that woke.</summary>
    [ProtoMember(1)] public uint EquipmentSlot { get; set; }

    /// <summary>The catalog modifier the affix woke as: a tier rise names the better tier's row.</summary>
    [ProtoMember(2)] public uint ModifierId { get; set; }

    [ProtoMember(3)] public AwakeningOutcome Outcome { get; set; }

    public static OutboundPacket Create(uint equipmentSlot, uint modifierId, AwakeningOutcome outcome, PacketEncoder encoder)
    {
        SItemAwakenedPacket packet = PacketEncoder.Scratch<SItemAwakenedPacket>();
        packet.EquipmentSlot = equipmentSlot;
        packet.ModifierId = modifierId;
        packet.Outcome = outcome;
        return encoder.Encode(packet, PacketType, Flags, Protocol);
    }
}
