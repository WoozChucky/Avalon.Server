using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.World;

public enum MapTransitionResult : byte
{
    Success       = 0,
    MapNotFound   = 1,
    NotNearPortal = 2,
    LevelTooLow   = 3,
    LevelTooHigh  = 4,
}

[ProtoContract]
public class SMapTransitionPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_MAP_TRANSITION;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public MapTransitionResult Result { get; set; }
    [ProtoMember(2)] public Guid InstanceId { get; set; }
    [ProtoMember(3)] public ushort MapId { get; set; }
    [ProtoMember(4)] public float SpawnX { get; set; }
    [ProtoMember(5)] public float SpawnY { get; set; }
    [ProtoMember(6)] public float SpawnZ { get; set; }
    [ProtoMember(7)] public string MapName { get; set; } = string.Empty;
    [ProtoMember(8)] public string MapDescription { get; set; } = string.Empty;

    public static NetworkPacket Create(
        MapTransitionResult result,
        Guid instanceId,
        ushort mapId,
        float spawnX,
        float spawnY,
        float spawnZ,
        string mapName,
        string mapDescription,
        Func<byte[], byte[]> encrypt)
    {
        using var memoryStream = new MemoryStream();

        var packet = new SMapTransitionPacket
        {
            Result         = result,
            InstanceId     = instanceId,
            MapId          = mapId,
            SpawnX         = spawnX,
            SpawnY         = spawnY,
            SpawnZ         = spawnZ,
            MapName        = mapName,
            MapDescription = mapDescription
        };

        Serializer.Serialize(memoryStream, packet);
        var buffer = encrypt(memoryStream.ToArray());

        return new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type     = PacketType,
                Flags    = Flags,
                Protocol = Protocol,
                Version  = 0
            },
            Payload = buffer
        };
    }

    /// <summary>Creates a failure response with only the result code populated.</summary>
    public static NetworkPacket CreateFailure(MapTransitionResult result, Func<byte[], byte[]> encrypt) =>
        Create(result, Guid.Empty, 0, 0f, 0f, 0f, string.Empty, string.Empty, encrypt);
}
