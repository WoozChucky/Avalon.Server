using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

/// <summary>
/// Why a world refused a version handshake it answered. Append-only: a client reads a value it does not know as a
/// refusal it cannot name.
/// </summary>
public enum WorldHandshakeRefusal
{
    /// <summary>Not refused, or refused for a reason the world does not tell (it then closes without a reply).</summary>
    None = 0,

    /// <summary>
    /// The client's version is older than the world accepts, or not a version at all.
    /// <see cref="SWorldHandshakePacket.MinVersion" /> names the oldest the world accepts.
    /// </summary>
    ClientVersionTooOld = 1,
}

[ProtoContract]
public class SWorldHandshakePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_WORLD_HANDSHAKE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public long? AccountId { get; set; }
    [ProtoMember(2)] public bool Verified { get; set; }

    /// <summary>
    /// Why the handshake was refused, with <see cref="Verified" /> false; the world closes the connection after the
    /// reply. <see cref="WorldHandshakeRefusal.None" />, and so absent on the wire, on an acceptance.
    /// </summary>
    [ProtoMember(3)] public WorldHandshakeRefusal Refusal { get; set; }

    /// <summary>
    /// The oldest client version the world accepts, on a <see cref="WorldHandshakeRefusal.ClientVersionTooOld" />
    /// refusal; null, and so absent on the wire, otherwise.
    /// </summary>
    [ProtoMember(4)] public string? MinVersion { get; set; }

    /// <summary>The world accepted the client (or, with <paramref name="verified" /> false, refused it unexplained).</summary>
    public static OutboundPacket Create(long accountId, bool verified, PacketEncoder encoder)
    {
        SWorldHandshakePacket message = PacketEncoder.Scratch<SWorldHandshakePacket>();
        message.AccountId = accountId;
        message.Verified = verified;
        message.Refusal = WorldHandshakeRefusal.None;
        message.MinVersion = null;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }

    /// <summary>The world refused the client for <paramref name="refusal" />, needing at least <paramref name="minVersion" />.</summary>
    public static OutboundPacket CreateRefusal(long accountId, WorldHandshakeRefusal refusal, string minVersion,
        PacketEncoder encoder)
    {
        SWorldHandshakePacket message = PacketEncoder.Scratch<SWorldHandshakePacket>();
        message.AccountId = accountId;
        message.Verified = false;
        message.Refusal = refusal;
        message.MinVersion = minVersion;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}
