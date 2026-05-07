using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Combat;

[ProtoContract]
public class SAbilityNotReadyPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_ABILITY_NOT_READY;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public uint AbilityId { get; set; }
    [ProtoMember(2)] public uint CooldownMs { get; set; }

    public static NetworkPacket Create(uint abilityId, uint cooldownMs, EncryptFunc encryptFunc)
        => PacketSerializationHelper.Serialize(
            new SAbilityNotReadyPacket { AbilityId = abilityId, CooldownMs = cooldownMs },
            PacketType, Flags, Protocol, encryptFunc);

}
