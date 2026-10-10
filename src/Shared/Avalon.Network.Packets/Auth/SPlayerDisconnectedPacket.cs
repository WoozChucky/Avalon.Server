using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class SPlayerDisconnectedPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CHARACTER_DISCONNECTED;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public ulong AccountId { get; set; }
    [ProtoMember(2)] public ulong CharacterId { get; set; }

    public static OutboundPacket Create(ulong accountId, ulong characterId, PacketEncoder encoder)
    {
        SPlayerDisconnectedPacket message = PacketEncoder.Scratch<SPlayerDisconnectedPacket>();
        message.AccountId = accountId;
        message.CharacterId = characterId;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}
