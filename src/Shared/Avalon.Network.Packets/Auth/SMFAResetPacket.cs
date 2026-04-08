using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class SMFAResetPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_MFA_RESET;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public MFAOperationResult Result { get; set; }

    public static NetworkPacket Create(MFAOperationResult result, Func<byte[], byte[]> encryptFunc)
    {
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, new SMFAResetPacket { Result = result });
        return new NetworkPacket
        {
            Header = new NetworkPacketHeader { Type = PacketType, Flags = Flags, Protocol = Protocol, Version = 0 },
            Payload = encryptFunc(ms.ToArray())
        };
    }
}
