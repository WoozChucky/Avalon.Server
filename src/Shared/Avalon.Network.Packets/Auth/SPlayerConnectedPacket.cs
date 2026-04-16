using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

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

    public static NetworkPacket Create(ulong accountId, ulong characterId, string name, EncryptFunc encryptFunc)
        => PacketSerializationHelper.Serialize(
            new SPlayerConnectedPacket { AccountId = accountId, CharacterId = characterId, Name = name },
            PacketType, Flags, Protocol, encryptFunc);
}
