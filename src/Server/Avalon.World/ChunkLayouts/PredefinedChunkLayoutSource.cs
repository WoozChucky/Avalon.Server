using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;

namespace Avalon.World.ChunkLayouts;

public class PredefinedChunkLayoutSource : IChunkLayoutSource
{
    private readonly IMapChunkPlacementRepository _repo;
    private readonly IChunkLibrary _library;

    public PredefinedChunkLayoutSource(IMapChunkPlacementRepository repo, IChunkLibrary library)
    {
        _repo = repo;
        _library = library;
    }

    public async Task<ChunkLayout> BuildAsync(MapTemplate template, CancellationToken ct)
    {
        var rows = await _repo.FindByMapAsync(template.Id, ct);
        if (rows.Count == 0)
            throw new InvalidOperationException(
                $"No MapChunkPlacement rows for town map {template.Id.Value}. Run Avalon.ChunkImporter.");

        var entryRows = rows.Where(r => r.IsEntry).ToList();
        if (entryRows.Count != 1)
            throw new InvalidOperationException(
                $"Town map {template.Id.Value} must have exactly one IsEntry placement (found {entryRows.Count}).");
        var entryRow = entryRows[0];

        var ids = rows.Select(r => r.ChunkTemplateId).Distinct().ToList();
        var byId = _library.LookupByIds(ids);

        var cellSize = byId[entryRow.ChunkTemplateId].CellSize;
        foreach (var r in rows)
        {
            var ct2 = byId[r.ChunkTemplateId];
            if (Math.Abs(ct2.CellSize - cellSize) > 0.001f)
                throw new InvalidOperationException(
                    $"Town map {template.Id.Value} chunk '{ct2.Name}' has CellSize={ct2.CellSize} != layout {cellSize}");
        }

        var placed = rows.Select(r => new PlacedChunk(
            r.ChunkTemplateId,
            r.GridX, r.GridZ, r.Rotation,
            new Vector3(r.GridX * cellSize, 0, r.GridZ * cellSize))).ToList();

        var entryPlaced = placed.Single(p => p.GridX == entryRow.GridX && p.GridZ == entryRow.GridZ);
        var entrySpawnWorld = TransformLocal(
            entryRow.EntryLocalX, entryRow.EntryLocalY, entryRow.EntryLocalZ,
            entryPlaced.WorldPos, entryPlaced.Rotation);

        var portals = BuildPortals(placed, byId);

        return new ChunkLayout(
            Seed: 0,
            Chunks: placed,
            EntryChunk: entryPlaced,
            BossChunk: null,
            Portals: portals,
            EntrySpawnWorldPos: entrySpawnWorld,
            CellSize: cellSize,
            Config: null);
    }

    // For predefined towns, every PortalSlot declared on a chunk template becomes a portal.
    // Target map id resolution: phase 1 uses TargetMapId = 0 (server treats 0 as "no target",
    // logs warning if traversed). Per-portal config table is future work for connecting
    // towns to dungeons.
    private static IReadOnlyList<PortalPlacement> BuildPortals(
        IReadOnlyList<PlacedChunk> chunks,
        IReadOnlyDictionary<ChunkTemplateId, ChunkTemplate> byId)
    {
        var result = new List<PortalPlacement>();
        foreach (var p in chunks)
        {
            var ct2 = byId[p.TemplateId];
            foreach (var slot in ct2.PortalSlots)
            {
                var world = TransformLocal(slot.LocalX, slot.LocalY, slot.LocalZ, p.WorldPos, p.Rotation);
                ushort targetMapId = 0; // TODO: resolve via per-portal config table once towns connect to dungeons
                result.Add(new PortalPlacement(slot.Role, world, targetMapId));
            }
        }
        return result;
    }

    private static Vector3 TransformLocal(float lx, float ly, float lz, Vector3 origin, byte rotation)
    {
        (float rx, float rz) = rotation switch
        {
            0 => (lx, lz),
            1 => (lz, -lx),
            2 => (-lx, -lz),
            3 => (-lz, lx),
            _ => (lx, lz),
        };
        return new Vector3(origin.x + rx, origin.y + ly, origin.z + rz);
    }
}
