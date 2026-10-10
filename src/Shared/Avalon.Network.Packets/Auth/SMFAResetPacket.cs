using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class SMFAResetPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_MFA_RESET;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public MFAOperationResult Result { get; set; }

    public static OutboundPacket Create(MFAOperationResult result, PacketEncoder encoder)
    {
        SMFAResetPacket message = PacketEncoder.Scratch<SMFAResetPacket>();
        message.Result = result;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}
