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
    {
        SMFAConfirmPacket message = PacketEncoder.Scratch<SMFAConfirmPacket>();
        message.RecoveryCodes = recoveryCodes;
        message.Result = result;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}
