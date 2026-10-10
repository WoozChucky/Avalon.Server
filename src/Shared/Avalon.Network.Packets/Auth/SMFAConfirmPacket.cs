using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class SMFAConfirmPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_MFA_CONFIRM;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public string[] RecoveryCodes { get; set; } = [];
    [ProtoMember(2)] public MFAOperationResult Result { get; set; }

    public static OutboundPacket Create(string[] recoveryCodes, MFAOperationResult result, PacketEncoder encoder)
        => encoder.Encode(
            new SMFAConfirmPacket { RecoveryCodes = recoveryCodes, Result = result },
            PacketType, Flags, Protocol);
}
