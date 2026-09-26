using Avalon.Network.Packets.Abilities;
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

    /// <summary>Unused since #164 and never set: there is no facing cone. Kept so the field number is not reused.</summary>
    [ProtoMember(7)] public float FacingAngle { get; set; }

    // Aim and shape (#164), so a client can draw the telegraph. Metres and degrees as on AbilityTemplate.
    [ProtoMember(8)] public AbilityAimMode AimMode { get; set; }
    [ProtoMember(9)] public AbilityShape Shape { get; set; }
    [ProtoMember(10)] public AbilityAnchor Anchor { get; set; }
    [ProtoMember(11)] public float Reach { get; set; }
    [ProtoMember(12)] public float Radius { get; set; }
    [ProtoMember(13)] public float ArcDegrees { get; set; }
    [ProtoMember(14)] public float ProjectileSpeed { get; set; }
    [ProtoMember(15)] public bool Pierce { get; set; }
    [ProtoMember(16)] public AbilityAffects Affects { get; set; }
}
