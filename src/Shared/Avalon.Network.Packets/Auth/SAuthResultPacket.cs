using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class SAuthResultPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_AUTH_RESULT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public ulong AccountId { get; set; }
    [ProtoMember(2)] public AuthResult Result { get; set; }
    [ProtoMember(3)] public string? MfaHash { get; set; }

    public static NetworkPacket Create(ulong? accountId, string? hash, AuthResult result, Func<byte[], byte[]> encryptFunc)
    {
        using var memoryStream = new MemoryStream();

        var packet = new SAuthResultPacket()
        {
            AccountId = accountId ?? 0,
            Result = result,
            MfaHash = hash
        };

        Serializer.Serialize(memoryStream, packet);

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
    ALREADY_CONNECTED
}
