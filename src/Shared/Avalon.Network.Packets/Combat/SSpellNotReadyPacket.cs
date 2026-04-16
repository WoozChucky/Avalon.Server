using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Combat;

[ProtoContract]
public class SSpellNotReadyPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_SPELL_NOT_READY;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public uint SpellId { get; set; }
    [ProtoMember(2)] public float Cooldown { get; set; }

    public static NetworkPacket Create(uint spellId, float cooldown, EncryptFunc encryptFunc)
        => PacketSerializationHelper.Serialize(
            new SSpellNotReadyPacket { SpellId = spellId, Cooldown = cooldown },
            PacketType, Flags, Protocol, encryptFunc);

}
