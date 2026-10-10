using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

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

    public static OutboundPacket Create(long? accountId, string? hash, AuthResult result,
        PacketEncoder encoder)
        => encoder.Encode(
            new SAuthResultPacket { AccountId = accountId ?? 0, Result = result, MfaHash = hash },
            PacketType, Flags, Protocol);
}

public enum AuthResult : ushort
{
    INVALID_CREDENTIALS,
    WRONG_KEY,
    MFA_REQUIRED,
    LOCKED,
    SUCCESS,
    ALREADY_CONNECTED,
    MFA_FAILED,
    // Appended, never inserted: these are wire ordinals.
    BANNED,
    DEACTIVATED
}
