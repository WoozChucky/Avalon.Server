using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Microsoft.Extensions.DependencyInjection;

namespace Avalon.World.ChunkLayouts;

public class PredefinedChunkLayoutSource : IChunkLayoutSource
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IChunkLibrary _library;

    /// <summary>
    /// DI ctor: <see cref="IMapChunkPlacementRepository"/> is Scoped (it depends on the
    /// EF <c>WorldDbContext</c>), so this Singleton resolves it per-call via
    /// <see cref="IServiceScopeFactory"/> rather than capturing a stale Scoped reference.
    /// Mirrors <see cref="ProceduralChunkLayoutSource"/>.
    /// </summary>
    public PredefinedChunkLayoutSource(IServiceScopeFactory scopeFactory, IChunkLibrary library)
    {
        _scopeFactory = scopeFactory;
        _library = library;
    }

    /// <summary>
    /// Test factory: builds a source against a substituted repository without forcing the
    /// caller to wire a real <see cref="IServiceScopeFactory"/>. Wraps the repo in a stand-in
    /// scope factory that returns the same instance on every call. Production code MUST NOT
    /// call this — use the DI ctor.
    /// </summary>
    /// <remarks>
    /// Lives as a static factory rather than a second public ctor because Microsoft.Extensions
    /// .DependencyInjection's <c>ServiceProvider</c> selects ctors by arity-then-resolvability
    /// and throws on ambiguity. <see cref="ActivatorUtilitiesConstructorAttribute"/> is honored
    /// only by <c>ActivatorUtilities.CreateInstance</c>, not by the container's own resolver,
    /// so two public 2-arg ctors here would crash host startup.
    /// </remarks>
    public static PredefinedChunkLayoutSource ForTesting(IMapChunkPlacementRepository repo, IChunkLibrary library)
        => new(new SingleRepoScopeFactory(repo), library);

    public async Task<ChunkLayout> BuildAsync(MapTemplate template, CancellationToken ct)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IMapChunkPlacementRepository repo = scope.ServiceProvider.GetRequiredService<IMapChunkPlacementRepository>();
        IReadOnlyList<MapChunkPlacement> rows = await repo.FindByMapAsync(template.Id, ct);
        if (rows.Count == 0)
            throw new InvalidOperationException(
                $"No MapChunkPlacement rows for town map {template.Id.Value}. Add Maps/TownLayouts/{template.Id.Value}.json; " +
                "the World server seeds it on start.");

        var entryRows = rows.Where(r => r.IsEntry).ToList();
        if (entryRows.Count != 1)
            throw new InvalidOperationException(
                $"Town map {template.Id.Value} must have exactly one IsEntry placement (found {entryRows.Count}).");
        MapChunkPlacement entryRow = entryRows[0];

        var ids = rows.Select(r => r.ChunkTemplateId).Distinct().ToList();
        IReadOnlyDictionary<ChunkTemplateId, ChunkTemplate> byId = _library.LookupByIds(ids);

        float cellSize = byId[entryRow.ChunkTemplateId].CellSize;
        foreach (MapChunkPlacement r in rows)
        {
            ChunkTemplate ct2 = byId[r.ChunkTemplateId];
            if (Math.Abs(ct2.CellSize - cellSize) > 0.001f)
                throw new InvalidOperationException(
                    $"Town map {template.Id.Value} chunk '{ct2.Name}' has CellSize={ct2.CellSize} != layout {cellSize}");
        }

        var placed = rows.Select(r => new PlacedChunk(
            r.ChunkTemplateId,
            r.GridX, r.GridZ, r.Rotation,
            new Vector3(r.GridX * cellSize, 0, r.GridZ * cellSize))).ToList();

        PlacedChunk entryPlaced = placed.Single(p => p.GridX == entryRow.GridX && p.GridZ == entryRow.GridZ);
        Vector3 entrySpawnWorld = ChunkRotation.LocalToWorld(
            entryRow.EntryLocalX, entryRow.EntryLocalY, entryRow.EntryLocalZ,
            entryPlaced.Rotation, cellSize, entryPlaced.WorldPos);

        // Index placements by (GridX, GridZ) so BuildPortals can look up the row that produced
        // each PlacedChunk and read its target columns.
        var rowByCell = rows.ToDictionary(r => (r.GridX, r.GridZ));

        IReadOnlyList<PortalPlacement> portals = BuildPortals(placed, byId, rowByCell, cellSize);

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

    // For predefined towns, each PortalSlot declared on a chunk template is emitted only when
    // the producing MapChunkPlacement row has a non-null target map id for that slot's role.
    // Slots without a configured target are skipped (chunk may be reused on a map that doesn't
    // route this portal).
    private static IReadOnlyList<PortalPlacement> BuildPortals(
        IReadOnlyList<PlacedChunk> chunks,
        IReadOnlyDictionary<ChunkTemplateId, ChunkTemplate> byId,
        IReadOnlyDictionary<(short GridX, short GridZ), MapChunkPlacement> rowByCell,
        float cellSize)
    {
        var result = new List<PortalPlacement>();
        foreach (PlacedChunk p in chunks)
        {
            ChunkTemplate ct2 = byId[p.TemplateId];
            MapChunkPlacement row = rowByCell[(p.GridX, p.GridZ)];
            foreach (ChunkPortalSlot slot in ct2.PortalSlots)
            {
                ushort? target = slot.Role switch
                {
                    PortalRole.Back => row.BackPortalTargetMapId,
                    PortalRole.Forward => row.ForwardPortalTargetMapId,
                    _ => null,
                };
                if (target is null) continue;

                Vector3 world = ChunkRotation.LocalToWorld(slot.LocalX, slot.LocalY, slot.LocalZ, p.Rotation, cellSize, p.WorldPos);
                result.Add(new PortalPlacement(slot.Role, world, target.Value));
            }
        }
        return result;
    }

    /// <summary>
    /// Test-only scope factory that hands out the same <see cref="IMapChunkPlacementRepository"/>
    /// each time, so the legacy two-arg test ctor still works without requiring the test author
    /// to wire a scope factory. Production uses the real Microsoft.Extensions.DI scope factory.
    /// </summary>
    private sealed class SingleRepoScopeFactory : IServiceScopeFactory
    {
        private readonly IMapChunkPlacementRepository _repo;
        public SingleRepoScopeFactory(IMapChunkPlacementRepository repo) => _repo = repo;

        public IServiceScope CreateScope() => new SingleRepoScope(_repo);

        private sealed class SingleRepoScope : IServiceScope, IServiceProvider
        {
            private readonly IMapChunkPlacementRepository _repo;
            public SingleRepoScope(IMapChunkPlacementRepository repo) => _repo = repo;
            public IServiceProvider ServiceProvider => this;
            public object? GetService(Type serviceType) =>
                serviceType == typeof(IMapChunkPlacementRepository) ? _repo : null;
            public void Dispose() { }
        }
    }
}
