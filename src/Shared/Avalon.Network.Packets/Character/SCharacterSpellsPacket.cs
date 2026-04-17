using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Character;

[ProtoContract]
public class SCharacterSpellsPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CHARACTER_SPELLS;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public SpellInfo[] Spells { get; set; }

    public static NetworkPacket Create(SpellInfo[] spells, EncryptFunc encrypt)
        => PacketSerializationHelper.Serialize(
            new SCharacterSpellsPacket { Spells = spells },
            PacketType, Flags, Protocol, encrypt);
}

[ProtoContract]
public class SpellInfo
{
    [ProtoMember(1)] public uint SpellId { get; set; }
    [ProtoMember(2)] public string Name { get; set; }
    [ProtoMember(3)] public float Cooldown { get; set; }
    [ProtoMember(4)] public float CastTime { get; set; }
    [ProtoMember(5)] public uint Cost { get; set; }
    [ProtoMember(6)] public ushort Range { get; set; }
}
