using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class SPlayerDisconnectedPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CHARACTER_DISCONNECTED;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public ulong AccountId { get; set; }
    [ProtoMember(2)] public ulong CharacterId { get; set; }

    public static NetworkPacket Create(ulong accountId, ulong characterId, EncryptFunc encryptFunc)
        => PacketSerializationHelper.Serialize(
            new SPlayerDisconnectedPacket { AccountId = accountId, CharacterId = characterId },
            PacketType, Flags, Protocol, encryptFunc);
}
