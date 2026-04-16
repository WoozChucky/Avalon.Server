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
    {
        using var memoryStream = new MemoryStream();

        var byePacket = new SPlayerDisconnectedPacket()
        {
            AccountId = accountId,
            CharacterId = characterId
        };

        Serializer.Serialize(memoryStream, byePacket);

        var buffer = encryptFunc(memoryStream.ToArray());

        return new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type = PacketType,
                Flags = Flags,
                Protocol = Protocol,
                Version = 0
            },
            Payload = buffer
        };
    }
}
