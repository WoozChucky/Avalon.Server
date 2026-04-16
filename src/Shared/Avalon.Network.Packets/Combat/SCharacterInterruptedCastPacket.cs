using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using NetworkPacketFlags = Avalon.Network.Packets.Abstractions.NetworkPacketFlags;
using NetworkProtocol = Avalon.Network.Packets.Abstractions.NetworkProtocol;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Combat;

[ProtoContract]
public class SCharacterInterruptedCastPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_SPELL_INTERRUPTED;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public ulong Caster { get; set; }
    [ProtoMember(2)] public uint SpellId { get; set; }

    public static NetworkPacket Create(ObjectGuid caster, SpellId spell, EncryptFunc encryptFunc)
        => PacketSerializationHelper.Serialize(
            new SCharacterInterruptedCastPacket { Caster = caster.RawValue, SpellId = spell.Value },
            PacketType, Flags, Protocol, encryptFunc);
}
