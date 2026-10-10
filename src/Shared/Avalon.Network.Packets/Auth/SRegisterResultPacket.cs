using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class SRegisterResultPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_REGISTER_RESULT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public RegisterResult Result { get; set; }

    public static OutboundPacket Create(RegisterResult result, PacketEncoder encoder)
    {
        SRegisterResultPacket message = PacketEncoder.Scratch<SRegisterResultPacket>();
        message.Result = result;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
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
