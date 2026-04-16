using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class SMFASetupPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_MFA_SETUP;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public string OtpUri { get; set; } = string.Empty;
    [ProtoMember(2)] public MFAOperationResult Result { get; set; }

    public static NetworkPacket Create(string otpUri, MFAOperationResult result, EncryptFunc encryptFunc)
    {
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, new SMFASetupPacket { OtpUri = otpUri, Result = result });
        return new NetworkPacket
        {
            Header = new NetworkPacketHeader { Type = PacketType, Flags = Flags, Protocol = Protocol, Version = 0 },
            Payload = encryptFunc(ms.ToArray())
        };
    }
}
