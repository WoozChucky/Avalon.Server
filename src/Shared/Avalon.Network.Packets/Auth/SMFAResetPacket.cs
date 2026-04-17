using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class SMFAResetPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_MFA_RESET;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public MFAOperationResult Result { get; set; }

    public static NetworkPacket Create(MFAOperationResult result, EncryptFunc encryptFunc)
        => PacketSerializationHelper.Serialize(
            new SMFAResetPacket { Result = result },
            PacketType, Flags, Protocol, encryptFunc);
}
