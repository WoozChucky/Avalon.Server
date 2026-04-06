using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

public enum WorldSelectResult : byte
{
    Success = 0,
    DuplicateSession = 1
}

[ProtoContract]
public class SWorldSelectPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_WORLD_SELECT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public byte[] WorldKey { get; set; }
    [ProtoMember(2)] public WorldSelectResult Result { get; set; }

    public static NetworkPacket Create(byte[] worldKey, Func<byte[], byte[]> encryptFunc)
    {
        using var memoryStream = new MemoryStream();

        var worldSelectPacket = new SWorldSelectPacket()
        {
            WorldKey = worldKey,
            Result = WorldSelectResult.Success
        };

        Serializer.Serialize(memoryStream, worldSelectPacket);

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

    public static NetworkPacket CreateError(WorldSelectResult result, Func<byte[], byte[]> encryptFunc)
    {
        using var memoryStream = new MemoryStream();

        var worldSelectPacket = new SWorldSelectPacket()
        {
            WorldKey = Array.Empty<byte>(),
            Result = result
        };

        Serializer.Serialize(memoryStream, worldSelectPacket);

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
