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

    public static OutboundPacket Create(uint serverVersion, byte[] publicKey, PacketEncoder encoder)
    {
        SServerInfoPacket message = PacketEncoder.Scratch<SServerInfoPacket>();
        message.Result = ServerInfoResult.Success;
        message.ServerVersion = serverVersion;
        message.PublicKey = publicKey;
        return encoder.Encode(message, PacketType, NetworkPacketFlags.ClearText, Protocol);
    }

    public static OutboundPacket CreateRejected(ServerInfoResult result, uint serverVersion, PacketEncoder encoder)
    {
        SServerInfoPacket message = PacketEncoder.Scratch<SServerInfoPacket>();
        message.Result = result;
        message.ServerVersion = serverVersion;
        message.PublicKey = Array.Empty<byte>();
        return encoder.Encode(message, PacketType, NetworkPacketFlags.ClearText, Protocol);
    }
}
