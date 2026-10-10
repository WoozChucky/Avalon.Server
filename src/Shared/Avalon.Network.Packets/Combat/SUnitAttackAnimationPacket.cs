using Avalon.Common;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;
using NetworkPacketFlags = Avalon.Network.Packets.Abstractions.NetworkPacketFlags;
using NetworkProtocol = Avalon.Network.Packets.Abstractions.NetworkProtocol;

namespace Avalon.Network.Packets.Combat;

[ProtoContract]
public class SUnitAttackAnimationPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CREATURE_ATTACK_ANIMATION;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public ulong Attacker { get; set; }
    [ProtoMember(2)] public ushort AnimationId { get; set; }

    public static OutboundPacket Create(ObjectGuid attacker, ushort animationId, PacketEncoder encoder)
        => encoder.Encode(
            new SUnitAttackAnimationPacket { Attacker = attacker.RawValue, AnimationId = animationId },
            PacketType, Flags, Protocol);
}
