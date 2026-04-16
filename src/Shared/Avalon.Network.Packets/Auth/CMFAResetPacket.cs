using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
[Packet(HandleOn = ComponentType.Auth, Type = NetworkPacketType.CMSG_MFA_RESET)]
public class CMFAResetPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_MFA_RESET;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public string RecoveryCode1 { get; set; } = string.Empty;
    [ProtoMember(2)] public string RecoveryCode2 { get; set; } = string.Empty;
    [ProtoMember(3)] public string RecoveryCode3 { get; set; } = string.Empty;

    public static NetworkPacket Create(string r1, string r2, string r3, EncryptFunc encryptFunc)
    {
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, new CMFAResetPacket { RecoveryCode1 = r1, RecoveryCode2 = r2, RecoveryCode3 = r3 });
        return new NetworkPacket
        {
            Header = new NetworkPacketHeader { Type = PacketType, Flags = Flags, Protocol = Protocol, Version = 0 },
            Payload = encryptFunc(ms.ToArray())
        };
    }
}
