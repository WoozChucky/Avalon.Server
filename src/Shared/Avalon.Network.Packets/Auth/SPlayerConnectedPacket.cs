using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class SPlayerConnectedPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CHARACTER_CONNECTED;
    private const NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;
    [ProtoMember(1)] public ulong AccountId { get; set; }
    [ProtoMember(2)] public ulong CharacterId { get; set; }
    [ProtoMember(3)] public string Name { get; set; }

    public static OutboundPacket Create(ulong accountId, ulong characterId, string name, PacketEncoder encoder)
    {
        SPlayerConnectedPacket message = PacketEncoder.Scratch<SPlayerConnectedPacket>();
        message.AccountId = accountId;
        message.CharacterId = characterId;
        message.Name = name;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}
