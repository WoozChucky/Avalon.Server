using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

public enum GameAdmissionResult { Accepted = 0, InvalidRequest = 1, AuthorizationRequired = 2, ServiceUnavailable = 3, UnsupportedClient = 4 }
[ProtoContract]
public sealed class SGameAdmissionPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_GAME_ADMISSION;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.ClearText;
    [ProtoMember(1)] public byte[] PublicKey { get; set; } = [];
    [ProtoMember(2)] public GameAdmissionResult Result { get; set; }

    /// <summary>
    /// Whether this world seals every gameplay payload with the session layer inside TLS (<c>Network:PacketEncryption</c>,
    /// #875). True: the client seals every gameplay packet it sends; one the world runs (the handshake, and every packet
    /// a session filter names but the pong) that arrives plain closes the connection, and an opcode the world never runs
    /// is dropped, whatever its flags. False: TLS alone, and the client sends plain; plain and sealed are both accepted.
    /// Absent, from a server before #875, reads false; such a server opens a packet by its header, sealed or not. A
    /// refusal says false: the connection closes.
    /// </summary>
    [ProtoMember(3)] public bool PacketEncryption { get; set; }

    public static OutboundPacket Create(byte[] publicKey, PacketEncoder encoder,
        GameAdmissionResult result = GameAdmissionResult.Accepted, bool packetEncryption = false)
    {
        SGameAdmissionPacket message = PacketEncoder.Scratch<SGameAdmissionPacket>();
        message.PublicKey = publicKey;
        message.Result = result;
        message.PacketEncryption = packetEncryption;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}
