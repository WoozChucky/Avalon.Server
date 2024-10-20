using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class CLogoutPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_LOGOUT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public ulong AccountId { get; set; }

    public static NetworkPacket Create(ulong accountId, Func<byte[], byte[]> encryptFunc)
    {
        using var memoryStream = new MemoryStream();

        var authPacket = new CLogoutPacket()
        {
            AccountId = accountId
        };

        Serializer.Serialize(memoryStream, authPacket);

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
