using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
[Packet(HandleOn = ComponentType.Auth, Type = NetworkPacketType.CMSG_MFA_CONFIRM)]
public class CMFAConfirmPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_MFA_CONFIRM;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public string Code { get; set; } = string.Empty;

    public static NetworkPacket Create(string code, EncryptFunc encryptFunc)
    {
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, new CMFAConfirmPacket { Code = code });
        return new NetworkPacket
        {
            Header = new NetworkPacketHeader { Type = PacketType, Flags = Flags, Protocol = Protocol, Version = 0 },
            Payload = encryptFunc(ms.ToArray())
        };
    }
}
