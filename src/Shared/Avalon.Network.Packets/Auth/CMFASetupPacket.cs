using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
[Packet(HandleOn = ComponentType.Auth, Type = NetworkPacketType.CMSG_MFA_SETUP)]
public class CMFASetupPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_MFA_SETUP;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    public static NetworkPacket Create(Func<byte[], byte[]> encryptFunc)
    {
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, new CMFASetupPacket());
        return new NetworkPacket
        {
            Header = new NetworkPacketHeader { Type = PacketType, Flags = Flags, Protocol = Protocol, Version = 0 },
            Payload = encryptFunc(ms.ToArray())
        };
    }
}
