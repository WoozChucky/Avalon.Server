using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class SRegisterResultPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_REGISTER_RESULT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public RegisterResult Result { get; set; }

    public static NetworkPacket Create(RegisterResult result, EncryptFunc encryptFunc)
        => PacketSerializationHelper.Serialize(
            new SRegisterResultPacket { Result = result },
            PacketType, Flags, Protocol, encryptFunc);
}

public enum RegisterResult : ushort
{
    UnknownError,
    EmptyUsername,
    EmptyEmail,
    EmptyPassword,
    DuplicateUsername,
    DuplicateEmail,
    PasswordTooShort,
    PasswordTooLong,
    InvalidEmail,
    Ok
}
