using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class SMFAConfirmPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_MFA_CONFIRM;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public string[] RecoveryCodes { get; set; } = [];
    [ProtoMember(2)] public MFAOperationResult Result { get; set; }

    public static NetworkPacket Create(string[] recoveryCodes, MFAOperationResult result, EncryptFunc encryptFunc)
        => PacketSerializationHelper.Serialize(
            new SMFAConfirmPacket { RecoveryCodes = recoveryCodes, Result = result },
            PacketType, Flags, Protocol, encryptFunc);
}
