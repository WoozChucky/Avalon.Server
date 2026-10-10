using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Auras;

/// <summary>
/// What changed this tick in one unit's auras (auras), in order: sent at most once a tick per unit, to the unit itself
/// and to every client that has it in view. A client that receives an update for a key it does not know ignores it.
/// </summary>
[ProtoContract]
public class SAuraUpdatePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_AURA_UPDATE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public ulong UnitGuid { get; set; }
    [ProtoMember(2)] public List<AuraEntryDto> Entries { get; set; } = [];

    public static OutboundPacket Create(ulong unitGuid, List<AuraEntryDto> entries, PacketEncoder encoder)
    {
        SAuraUpdatePacket message = PacketEncoder.Scratch<SAuraUpdatePacket>();
        message.UnitGuid = unitGuid;
        message.Entries = entries;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}
