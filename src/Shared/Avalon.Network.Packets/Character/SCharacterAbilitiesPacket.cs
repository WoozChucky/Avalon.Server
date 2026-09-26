using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Character;

[ProtoContract]
public class SCharacterAbilitiesPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CHARACTER_ABILITIES;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public AbilityInfo[] Abilities { get; set; }

    public static NetworkPacket Create(AbilityInfo[] abilities, EncryptFunc encrypt)
        => PacketSerializationHelper.Serialize(
            new SCharacterAbilitiesPacket { Abilities = abilities },
            PacketType, Flags, Protocol, encrypt);
}

[ProtoContract]
public class AbilityInfo
{
    [ProtoMember(1)] public uint AbilityId { get; set; }
    [ProtoMember(2)] public string Name { get; set; }
    [ProtoMember(3)] public float Cooldown { get; set; }
    [ProtoMember(4)] public float CastTime { get; set; }
    [ProtoMember(5)] public uint Cost { get; set; }
    [ProtoMember(6)] public ushort Range { get; set; }

    /// <summary>
    /// The facing cone's half-angle, in degrees from the caster's facing (#513). A cast at a target
    /// is accepted only when the angle between the caster's facing and the direction to the target
    /// is strictly less than this value; a target at exactly this angle is refused, with
    /// CastRejectReason.NotFacing. Targetless casts are not checked.
    /// </summary>
    [ProtoMember(7)] public float FacingAngle { get; set; }
}
