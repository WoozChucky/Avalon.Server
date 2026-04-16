using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Generic;

/// <summary>
/// Sent by the server immediately before closing a client connection.
/// Transmitted unencrypted so it is readable regardless of crypto session state.
/// </summary>
[ProtoContract]
public class SDisconnectPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_DISCONNECT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.None;

    [ProtoMember(1)] public string Reason { get; set; } = string.Empty;
    [ProtoMember(2)] public DisconnectReason ReasonCode { get; set; }

    public static NetworkPacket Create(string reason, DisconnectReason reasonCode)
        => PacketSerializationHelper.SerializeUnencrypted(
            new SDisconnectPacket { Reason = reason, ReasonCode = reasonCode },
            PacketType, Flags, Protocol);
}

public enum DisconnectReason : ushort
{
    Unknown = 0,
    ServerShutdown = 1,
    DuplicateLogin = 2,
    Kicked = 3,
}
