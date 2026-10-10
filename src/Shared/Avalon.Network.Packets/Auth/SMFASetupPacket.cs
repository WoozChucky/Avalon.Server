using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class SMFASetupPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_MFA_SETUP;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public string OtpUri { get; set; } = string.Empty;
    [ProtoMember(2)] public MFAOperationResult Result { get; set; }

    public static OutboundPacket Create(string otpUri, MFAOperationResult result, PacketEncoder encoder)
        => encoder.Encode(
            new SMFASetupPacket { OtpUri = otpUri, Result = result },
            PacketType, Flags, Protocol);
}
