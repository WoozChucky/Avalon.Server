using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Social;

[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_CHAT_MESSAGE)]
public class CChatMessagePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_CHAT_MESSAGE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public string Message { get; set; }
    [ProtoMember(2)] public DateTime DateTime { get; set; }

    public static NetworkPacket Create(string message, DateTime dateTime, EncryptFunc encryptFunc)
    {
        using var memoryStream = new MemoryStream();

        var p = new CChatMessagePacket()
        {
            Message = message,
            DateTime = dateTime
        };

        Serializer.Serialize(memoryStream, p);

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
