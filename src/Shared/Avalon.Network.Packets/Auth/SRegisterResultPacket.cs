using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class SRegisterResultPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_REGISTER_RESULT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;
    
    [ProtoMember(1)] public RegisterResult Result { get; set; }

    public static NetworkPacket Create(RegisterResult result, Func<byte[], byte[]> encryptFunc)
    {
        using var memoryStream = new MemoryStream();
        
        var authPacket = new SRegisterResultPacket()
        {
            Result = result
        };
        
        Serializer.Serialize(memoryStream, authPacket);
        
        var buffer = encryptFunc(memoryStream.ToArray());
        
        return new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type = PacketType,
                Flags = Flags,
                Protocol = Protocol,
                Version = 0
            },
            Payload = buffer
        };
    }
}

public enum RegisterResult : ushort
{
    UnknownError,
    EmptyUsername,
    EmptyPassword,
    DuplicateUsername,
    DuplicateEmail,
    PasswordTooShort,
    PasswordTooLong,
    InvalidEmail,
    Ok
}
