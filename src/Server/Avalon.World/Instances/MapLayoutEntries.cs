using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.World;
using Avalon.World.ChunkLayouts;

namespace Avalon.World.Instances;

/// <summary>
/// One instance's layout as <c>SMSG_CHUNK_LAYOUT</c> carries it (#883): an entry per chunk, with the chunk's name, an
/// entry per portal, one per solid doodad's box and one per scenery piece in the ring around it, with the ring's piece
/// names, each once, that those entries index. Built and encoded once, off the tick, at the end of the instance's
/// construction (#1130). Only the encoded packet survives; the temporary DTOs are not retained by the instance.
/// </summary>
/// <remarks>
/// <para>
/// What it is built from does not change while the instance lives: an instance keeps the <see cref="ChunkLayout" /> it
/// was built from (a town's from its placement rows, a procedural map's from its seed, with the boxes its doodad plan
/// gave it before the bake), and the chunk library that names the chunks is loaded once, with the world. Neither is an
/// area <c>/reload</c> or live template editing replaces.
/// </para>
/// <para>
/// The packet's thread-local message lets go of the lists after encoding, also on failure. The returned packet owns
/// one pooled reference: the instance keeps it until disposal, and each arrival takes a share for its connection.
/// </para>
/// </remarks>
internal static class MapLayoutEntries
{
    /// <summary>
    /// The entries of <paramref name="layout" />, in its order, for the instance <paramref name="instanceId" />;
    /// throws for a chunk <paramref name="chunkLibrary" /> does not hold.
    /// </summary>
    public static OutboundPacket Encode(Guid instanceId, ushort mapId, ChunkLayout layout, IChunkLibrary chunkLibrary,
        PacketEncoder encoder)
    {
        IReadOnlyList<PlacedChunk> placed = layout.Chunks;
        var chunks = new List<PlacedChunkDto>(placed.Count);
        for (int i = 0; i < placed.Count; i++)
        {
            PlacedChunk chunk = placed[i];
            chunks.Add(new PlacedChunkDto
            {
                ChunkTemplateId = chunk.TemplateId.Value,
                ChunkName = chunkLibrary.GetById(chunk.TemplateId).Name,
                GridX = chunk.GridX,
                GridZ = chunk.GridZ,
                Rotation = chunk.Rotation,
            });
        }

        IReadOnlyList<PortalPlacement> placements = layout.Portals;
        var portals = new List<PortalPlacementDto>(placements.Count);
        for (int i = 0; i < placements.Count; i++)
        {
            PortalPlacement portal = placements[i];
            portals.Add(new PortalPlacementDto
            {
                Role = (byte)portal.Role,
                WorldPos = Vector3Dto.From(portal.WorldPos),
                Radius = portal.Radius,
                TargetMapId = portal.TargetMapId,
            });
        }

        IReadOnlyList<DoodadObstacle> boxes = layout.Obstacles ?? [];
        var obstacles = new List<DoodadObstacleDto>(boxes.Count);
        for (int i = 0; i < boxes.Count; i++)
        {
            DoodadObstacle box = boxes[i];
            obstacles.Add(new DoodadObstacleDto
            {
                MinX = box.MinX,
                MaxX = box.MaxX,
                MinZ = box.MinZ,
                MaxZ = box.MaxZ,
                BaseY = box.BaseY,
                TopY = box.TopY,
            });
        }

        // Each piece's name goes on the wire once, in the order the ring first uses it; a placement carries its index.
        IReadOnlyList<SceneryPlacement> pieces = layout.Scenery;
        var scenery = new List<SceneryPlacementDto>(pieces.Count);
        var sceneryNames = new List<string>();
        var nameIndex = new Dictionary<string, uint>(StringComparer.Ordinal);
        for (int i = 0; i < pieces.Count; i++)
        {
            SceneryPlacement piece = pieces[i];
            if (!nameIndex.TryGetValue(piece.Name, out uint index))
            {
                index = (uint)sceneryNames.Count;
                nameIndex.Add(piece.Name, index);
                sceneryNames.Add(piece.Name);
            }

            scenery.Add(new SceneryPlacementDto
            {
                NameIndex = index,
                GridX = checked((short)piece.GridX),
                GridZ = checked((short)piece.GridZ),
                Rotation = checked((byte)piece.Rotation),
            });
        }

        return SChunkLayoutPacket.Create(layout.Seed, instanceId, mapId, layout.CellSize, chunks, layout.EntrySpawnWorldPos,
            portals, obstacles, scenery, sceneryNames, encoder);
    }
}
