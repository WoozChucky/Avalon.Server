using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Social;

[ProtoContract]
public class COpenChatPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_CHAT_OPEN;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public ulong AccountId { get; set; }
    [ProtoMember(2)] public ulong CharacterId { get; set; }

    public static NetworkPacket Create(ulong accountId, ulong characterId, Func<byte[], byte[]> encryptFunc)
    {
        using var memoryStream = new MemoryStream();

        var movementPacket = new COpenChatPacket()
        {
            AccountId = accountId,
            CharacterId = characterId
        };

        Serializer.Serialize(memoryStream, movementPacket);

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
