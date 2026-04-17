using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
[Packet(HandleOn = ComponentType.Auth, Type = NetworkPacketType.CMSG_WORLD_LIST)]
public class CWorldListPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_WORLD_LIST;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    public static NetworkPacket Create(EncryptFunc encryptFunc)
    {
        using var memoryStream = new MemoryStream();

        var worldListPacket = new CWorldListPacket()
        {
        };

        Serializer.Serialize(memoryStream, worldListPacket);

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
