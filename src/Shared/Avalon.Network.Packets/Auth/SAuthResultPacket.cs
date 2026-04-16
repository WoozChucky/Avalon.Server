using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class SAuthResultPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_AUTH_RESULT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public long AccountId { get; set; }
    [ProtoMember(2)] public AuthResult Result { get; set; }
    [ProtoMember(3)] public string? MfaHash { get; set; }

    public static NetworkPacket Create(long? accountId, string? hash, AuthResult result,
        EncryptFunc encryptFunc)
    {
        using MemoryStream memoryStream = new();

        SAuthResultPacket packet = new() {AccountId = accountId ?? 0, Result = result, MfaHash = hash};

        Serializer.Serialize(memoryStream, packet);

        byte[]? buffer = encryptFunc(memoryStream.ToArray());

        return new NetworkPacket
        {
            Header = new NetworkPacketHeader {Type = PacketType, Flags = Flags, Protocol = Protocol, Version = 0},
            Payload = buffer.ToArray()
        };
    }
}

public enum AuthResult : ushort
{
    INVALID_CREDENTIALS,
    WRONG_KEY,
    MFA_REQUIRED,
    LOCKED,
    SUCCESS,
    ALREADY_CONNECTED,
    MFA_FAILED
}
