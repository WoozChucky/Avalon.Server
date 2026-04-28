using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.World;

[ProtoContract]
public class PlacedChunkDto
{
    [ProtoMember(1)] public int ChunkTemplateId { get; set; }
    [ProtoMember(2)] public short GridX { get; set; }
    [ProtoMember(3)] public short GridZ { get; set; }
    [ProtoMember(4)] public byte Rotation { get; set; }
    [ProtoMember(5)] public string ChunkName { get; set; } = string.Empty;
}

[ProtoContract]
public class Vector3Dto
{
    [ProtoMember(1)] public float X { get; set; }
    [ProtoMember(2)] public float Y { get; set; }
    [ProtoMember(3)] public float Z { get; set; }

    public static Vector3Dto From(Vector3 v) => new() { X = v.x, Y = v.y, Z = v.z };
}

[ProtoContract]
public class PortalPlacementDto
{
    [ProtoMember(1)] public byte Role { get; set; }
    [ProtoMember(2)] public Vector3Dto WorldPos { get; set; } = new();
    [ProtoMember(3)] public float Radius { get; set; }
    [ProtoMember(4)] public ushort TargetMapId { get; set; }
}

[ProtoContract]
public class SChunkLayoutPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CHUNK_LAYOUT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public int Seed { get; set; }
    [ProtoMember(2)] public Guid InstanceId { get; set; }
    [ProtoMember(3)] public float CellSize { get; set; }
    [ProtoMember(4)] public List<PlacedChunkDto> Chunks { get; set; } = new();
    [ProtoMember(5)] public Vector3Dto EntrySpawn { get; set; } = new();
    [ProtoMember(6)] public List<PortalPlacementDto> Portals { get; set; } = new();

    public static NetworkPacket Create(
        int seed,
        Guid instanceId,
        float cellSize,
        IReadOnlyList<PlacedChunkDto> chunks,
        Vector3 entrySpawn,
        IReadOnlyList<PortalPlacementDto> portals,
        EncryptFunc encrypt)
    {
        var pkt = new SChunkLayoutPacket
        {
            Seed = seed,
            InstanceId = instanceId,
            CellSize = cellSize,
            Chunks = chunks.ToList(),
            EntrySpawn = Vector3Dto.From(entrySpawn),
            Portals = portals.ToList(),
        };
        return PacketSerializationHelper.Serialize(pkt, PacketType, Flags, Protocol, encrypt);
    }
}
