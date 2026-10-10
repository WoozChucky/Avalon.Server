using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.State;
using ProtoBuf;

namespace Avalon.Network.Packets.Party;

/// <summary>A party member's pools, to the other members in its instance only, on change, at most four times a second.</summary>
[ProtoContract]
public class SPartyMemberStatusPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_PARTY_MEMBER_STATUS;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public uint CharacterId { get; set; }
    [ProtoMember(2)] public uint Health { get; set; }
    [ProtoMember(3)] public uint MaxHealth { get; set; }
    [ProtoMember(4)] public uint Power { get; set; }
    [ProtoMember(5)] public uint MaxPower { get; set; }
    [ProtoMember(6)] public PowerType PowerType { get; set; }
    [ProtoMember(7)] public bool IsDead { get; set; }

    public static OutboundPacket Create(uint characterId, uint health, uint maxHealth, uint power, uint maxPower,
        PowerType powerType, bool isDead, PacketEncoder encoder)
    {
        SPartyMemberStatusPacket message = PacketEncoder.Scratch<SPartyMemberStatusPacket>();
        message.CharacterId = characterId;
        message.Health = health;
        message.MaxHealth = maxHealth;
        message.Power = power;
        message.MaxPower = maxPower;
        message.PowerType = powerType;
        message.IsDead = isDead;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}
