using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Auras;

/// <summary>
/// Every aura one unit holds (auras): sent when the unit comes into a client's view, and for a client's own character
/// whenever it enters an instance or the world, empty too. A client replaces what it held for that unit with it.
/// </summary>
[ProtoContract]
public class SAuraListPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_AURA_LIST;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public ulong UnitGuid { get; set; }
    [ProtoMember(2)] public List<AuraEntryDto> Entries { get; set; } = [];

    public static OutboundPacket Create(ulong unitGuid, List<AuraEntryDto> entries, PacketEncoder encoder)
    {
        SAuraListPacket message = PacketEncoder.Scratch<SAuraListPacket>();
        message.UnitGuid = unitGuid;
        message.Entries = entries;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}
