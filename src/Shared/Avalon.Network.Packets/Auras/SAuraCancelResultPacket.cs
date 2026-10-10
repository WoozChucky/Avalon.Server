using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Auras;

/// <summary>The one answer to a CMSG_AURA_CANCEL (auras), echoing the aura it named.</summary>
[ProtoContract]
public class SAuraCancelResultPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_AURA_CANCEL_RESULT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public uint AuraId { get; set; }
    [ProtoMember(2)] public AuraCancelResult Result { get; set; }

    public static OutboundPacket Create(uint auraId, AuraCancelResult result, PacketEncoder encoder)
    {
        SAuraCancelResultPacket message = PacketEncoder.Scratch<SAuraCancelResultPacket>();
        message.AuraId = auraId;
        message.Result = result;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}
