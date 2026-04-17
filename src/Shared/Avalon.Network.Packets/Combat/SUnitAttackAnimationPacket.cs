using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using NetworkPacketFlags = Avalon.Network.Packets.Abstractions.NetworkPacketFlags;
using NetworkProtocol = Avalon.Network.Packets.Abstractions.NetworkProtocol;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Combat;

[ProtoContract]
public class SUnitAttackAnimationPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CREATURE_ATTACK_ANIMATION;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public ulong Attacker { get; set; }
    [ProtoMember(2)] public ushort AnimationId { get; set; }

    public static NetworkPacket Create(ObjectGuid attacker, ushort animationId, EncryptFunc encryptFunc)
        => PacketSerializationHelper.Serialize(
            new SUnitAttackAnimationPacket { Attacker = attacker.RawValue, AnimationId = animationId },
            PacketType, Flags, Protocol, encryptFunc);
}
