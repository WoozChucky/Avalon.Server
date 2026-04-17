using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Handshake;

public enum ServerInfoResult
{
    Success = 0,
    ClientVersionTooOld = 1
}

[ProtoContract]
public class SServerInfoPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_SERVER_INFO;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;

    [ProtoMember(1)] public uint ServerVersion { get; set; }
    [ProtoMember(2)] public byte[] PublicKey { get; set; }
    [ProtoMember(3)] public ServerInfoResult Result { get; set; }

    public static NetworkPacket Create(uint serverVersion, byte[] publicKey)
        => PacketSerializationHelper.SerializeUnencrypted(
            new SServerInfoPacket { Result = ServerInfoResult.Success, ServerVersion = serverVersion, PublicKey = publicKey },
            PacketType, NetworkPacketFlags.ClearText, Protocol);

    public static NetworkPacket CreateRejected(ServerInfoResult result, uint serverVersion)
        => PacketSerializationHelper.SerializeUnencrypted(
            new SServerInfoPacket { Result = result, ServerVersion = serverVersion, PublicKey = Array.Empty<byte>() },
            PacketType, NetworkPacketFlags.ClearText, Protocol);
}
