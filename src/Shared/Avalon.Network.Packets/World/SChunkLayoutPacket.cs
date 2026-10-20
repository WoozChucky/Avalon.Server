using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.World;

/// <summary>
/// One chunk of the layout: its template's id and name, the cell it stands in and its rotation in quarter turns. A
/// procedural map's cells may be negative, so they are zigzag-encoded (<c>sint32</c>): a cell near the origin costs its
/// tag and one byte, either side of it.
/// </summary>
[ProtoContract]
public class PlacedChunkDto
{
    [ProtoMember(1)] public int ChunkTemplateId { get; set; }
    [ProtoMember(2, DataFormat = DataFormat.ZigZag)] public short GridX { get; set; }
    [ProtoMember(3, DataFormat = DataFormat.ZigZag)] public short GridZ { get; set; }
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
    // The wire contract stays as it is: IsRequired would change what is serialized (WireSchemaShould pins it).
#pragma warning disable PBN0022
    [ProtoMember(2)] public Vector3Dto WorldPos { get; set; } = new();
#pragma warning restore PBN0022
    [ProtoMember(3)] public float Radius { get; set; }
    [ProtoMember(4)] public ushort TargetMapId { get; set; }
}

/// <summary>
/// A solid doodad's box, as the server's navmesh bake cuts it out: its world X/Z bounds, the height it stands at and its
/// top. A client bakes the same box into its own navmesh, with the vertices and faces the server writes
/// (DoodadObstacleGeometry).
/// </summary>
[ProtoContract]
public class DoodadObstacleDto
{
    [ProtoMember(1)] public float MinX { get; set; }
    [ProtoMember(2)] public float MaxX { get; set; }
    [ProtoMember(3)] public float MinZ { get; set; }
    [ProtoMember(4)] public float MaxZ { get; set; }
    [ProtoMember(5)] public float BaseY { get; set; }
    [ProtoMember(6)] public float TopY { get; set; }
}

/// <summary>
/// One scenery piece drawn in a ring cell around the map: the piece, as its index into the packet's
/// <see cref="SChunkLayoutPacket.SceneryNames" />, the cell it stands in and its rotation in quarter turns. A ring cell
/// lies outside the map's own cells, so it is often negative: the cell is zigzag-encoded (<c>sint32</c>), a cell near
/// the origin costing its tag and one byte either side of it. Never walked and never baked into a navmesh.
/// </summary>
[ProtoContract]
public class SceneryPlacementDto
{
    [ProtoMember(1)] public uint NameIndex { get; set; }
    [ProtoMember(2, DataFormat = DataFormat.ZigZag)] public short GridX { get; set; }
    [ProtoMember(3, DataFormat = DataFormat.ZigZag)] public short GridZ { get; set; }
    [ProtoMember(4)] public byte Rotation { get; set; }
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
    // The wire contract stays as it is: IsRequired would change what is serialized (WireSchemaShould pins it).
#pragma warning disable PBN0022
    [ProtoMember(5)] public Vector3Dto EntrySpawn { get; set; } = new();
#pragma warning restore PBN0022
    [ProtoMember(6)] public List<PortalPlacementDto> Portals { get; set; } = new();
    [ProtoMember(7)] public ushort MapId { get; set; }

    /// <summary>The solid doodads' boxes, in the order the bake appends them; empty for a map without one.</summary>
    [ProtoMember(8)] public List<DoodadObstacleDto> Obstacles { get; set; } = new();

    /// <summary>
    /// The scenery ring: drawn, never walked, never baked. Each placement names its piece by an index into
    /// <see cref="SceneryNames" />.
    /// </summary>
    [ProtoMember(9)] public List<SceneryPlacementDto> Scenery { get; set; } = new();

    /// <summary>
    /// The scenery ring's piece names, each once, in the order <see cref="Scenery" /> first uses them: a placement's
    /// <see cref="SceneryPlacementDto.NameIndex" /> is its piece's index here. Empty for a map without a ring.
    /// </summary>
    [ProtoMember(10)] public List<string> SceneryNames { get; set; } = new();

    /// <summary>
    /// Encodes an instance's layout. <paramref name="chunks" />, <paramref name="portals" />,
    /// <paramref name="obstacles" />, <paramref name="scenery" /> and <paramref name="sceneryNames" /> are written as
    /// they are and not copied, so a caller may hand in the same lists for every packet of one layout (#883): the
    /// message holds them only while it is encoded, and neither they nor their entries are changed. They are lists, as
    /// every other factory takes its entries, so that nothing of the caller's runs once the scratch message is taken
    /// (#875).
    /// </summary>
    public static OutboundPacket Create(
        int seed,
        Guid instanceId,
        ushort mapId,
        float cellSize,
        List<PlacedChunkDto> chunks,
        Vector3 entrySpawn,
        List<PortalPlacementDto> portals,
        List<DoodadObstacleDto> obstacles,
        List<SceneryPlacementDto> scenery,
        List<string> sceneryNames,
        PacketEncoder encoder)
    {
        SChunkLayoutPacket message = PacketEncoder.Scratch<SChunkLayoutPacket>();
        message.Seed = seed;
        message.InstanceId = instanceId;
        message.MapId = mapId;
        message.CellSize = cellSize;
        message.Chunks = chunks;
        message.EntrySpawn = Vector3Dto.From(entrySpawn);
        message.Portals = portals;
        message.Obstacles = obstacles;
        message.Scenery = scenery;
        message.SceneryNames = sceneryNames;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}
