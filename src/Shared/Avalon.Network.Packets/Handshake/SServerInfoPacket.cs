using Avalon.Network.Packets.Abstractions;
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
    {
        using var memoryStream = new MemoryStream();

        var packet = new SServerInfoPacket()
        {
            Result = ServerInfoResult.Success,
            ServerVersion = serverVersion,
            PublicKey = publicKey
        };

        Serializer.Serialize(memoryStream, packet);

        memoryStream.TryGetBuffer(out var buffer);

        return new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type = PacketType,
                Flags = NetworkPacketFlags.ClearText,
                Protocol = Protocol,
                Version = 0
            },
            Payload = buffer.ToArray()
        };
    }

    public static NetworkPacket CreateRejected(ServerInfoResult result, uint serverVersion)
    {
        using var memoryStream = new MemoryStream();

        var packet = new SServerInfoPacket()
        {
            Result = result,
            ServerVersion = serverVersion,
            PublicKey = Array.Empty<byte>()
        };

        Serializer.Serialize(memoryStream, packet);

        memoryStream.TryGetBuffer(out var buffer);

        return new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type = PacketType,
                Flags = NetworkPacketFlags.ClearText,
                Protocol = Protocol,
                Version = 0
            },
            Payload = buffer.ToArray()
        };
    }
}
