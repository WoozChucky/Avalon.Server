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

    public static NetworkPacket Create(uint auraId, AuraCancelResult result, EncryptFunc encrypt)
        => PacketSerializationHelper.Serialize(new SAuraCancelResultPacket { AuraId = auraId, Result = result },
            PacketType, Flags, Protocol, encrypt);
}
