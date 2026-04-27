using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Avalon.World.ChunkLayouts;

public class ProceduralChunkLayoutSource : IChunkLayoutSource
{
    private const int MaxRetries = 3;
    private readonly ILogger<ProceduralChunkLayoutSource> _logger;
    private readonly IChunkLibrary _library;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Random _seedRng = new();
    private readonly object _seedLock = new();

    public ProceduralChunkLayoutSource(
        ILoggerFactory loggerFactory,
        IChunkLibrary library,
        IServiceScopeFactory scopeFactory)
    {
        _logger = loggerFactory.CreateLogger<ProceduralChunkLayoutSource>();
        _library = library;
        _scopeFactory = scopeFactory;
    }

    public async Task<ChunkLayout> BuildAsync(MapTemplate template, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var configRepo = scope.ServiceProvider.GetRequiredService<IProceduralMapConfigRepository>();
        var config = await configRepo.FindByTemplateIdAsync(template.Id, ct)
            ?? throw new InvalidProceduralConfigException(
                $"No ProceduralMapConfig for map {template.Id.Value}");

        var pool = _library.GetByPool(config.ChunkPoolId);
        return Generate(config, pool, NextSeed());
    }

    private int NextSeed()
    {
        lock (_seedLock) return _seedRng.Next();
    }

    /// <summary>
    /// Deterministic generation entry point. Public so unit tests and benchmarks can drive the
    /// algorithm directly with a fixed seed without arranging the full <see cref="BuildAsync"/>
    /// dependency graph (config repository, scope factory). Production code goes through
    /// <see cref="BuildAsync"/> which loads the config and calls this internally.
    /// </summary>
    public ChunkLayout Generate(ProceduralMapConfig config, IReadOnlyList<ChunkPoolMember> pool, int seed)
    {
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            int attemptSeed = seed + attempt;
            if (TryGenerate(config, pool, attemptSeed, out var layout, out var error))
                return layout!;
            _logger.LogWarning("Procedural generation attempt {Attempt} failed: {Error}", attempt + 1, error);
        }
        throw new ProceduralGenerationFailedException(
            $"Failed to generate layout for map {config.MapTemplateId.Value} after {MaxRetries} attempts");
    }

    private bool TryGenerate(ProceduralMapConfig cfg, IReadOnlyList<ChunkPoolMember> pool, int seed,
        out ChunkLayout? layout, out string? error)
    {
        layout = null; error = null;
        var rng = new Random(seed);
        int pathLen = rng.Next(cfg.MainPathMin, cfg.MainPathMax + 1);

        // 1. Pick entry chunk
        var entryCandidates = pool.Where(m =>
            m.Template.SpawnSlots.Any(s => s.Tag.Equals("entry", StringComparison.OrdinalIgnoreCase)) &&
            m.Template.PortalSlots.Any(p => p.Role == PortalRole.Back)).ToList();
        if (entryCandidates.Count == 0) { error = "No entry-capable chunks"; return false; }

        var entryMember = WeightedPick(entryCandidates, rng);
        var mainPath = new List<PlacedChunkRecord>
        {
            new(entryMember.Template, 0, 0, 0)
        };
        var grid = new Dictionary<(int, int), PlacedChunkRecord> { [(0, 0)] = mainPath[0] };

        // 2. Walk main path
        for (int step = 1; step < pathLen; step++)
        {
            bool requiredForward = step == pathLen - 1 && cfg.ForwardPortalTargetMapId is not null;
            bool requiredBoss    = step == pathLen - 1 && cfg.HasBoss;

            if (!TryAttachNextChunk(mainPath[^1], pool, grid, rng, requiredBoss, requiredForward, out var placed))
            {
                error = $"Could not attach at step {step}";
                return false;
            }
            mainPath.Add(placed!);
            grid[(placed!.GridX, placed.GridZ)] = placed;
        }

        // 3. Branches
        for (int i = 1; i < mainPath.Count - 1; i++)
        {
            if (rng.NextDouble() >= cfg.BranchChance) continue;
            int branchLen = rng.Next(1, cfg.BranchMaxDepth + 1);
            var tail = mainPath[i];
            for (int b = 0; b < branchLen; b++)
            {
                if (!TryAttachNextChunk(tail, pool, grid, rng, requiredBoss: false, requiredForward: false, out var placed))
                    break;
                grid[(placed!.GridX, placed.GridZ)] = placed;
                tail = placed;
            }
        }

        // 4. Assemble
        float cellSize = entryMember.Template.CellSize;
        var placedChunks = grid.Values.Select(r => new PlacedChunk(
            r.Template.Id, (short)r.GridX, (short)r.GridZ, r.Rotation,
            new Vector3(r.GridX * cellSize, 0, r.GridZ * cellSize))).ToList();

        var entry = placedChunks.First(p => p.GridX == 0 && p.GridZ == 0);
        var bossRec = cfg.HasBoss ? mainPath[^1] : null;
        var boss = bossRec is null ? null : placedChunks.First(p => p.GridX == bossRec.GridX && p.GridZ == bossRec.GridZ);

        var entrySlot = entryMember.Template.SpawnSlots.First(s => s.Tag.Equals("entry", StringComparison.OrdinalIgnoreCase));
        var entrySpawnWorldPos = TransformLocal(entrySlot.LocalX, entrySlot.LocalY, entrySlot.LocalZ, entry.WorldPos, entry.Rotation);

        var portals = BuildPortals(entry, entryMember.Template, boss, bossRec?.Template, cfg);

        layout = new ChunkLayout(seed, placedChunks, entry, boss, portals, entrySpawnWorldPos, cellSize, Config: cfg);
        return true;
    }

    private static bool TryAttachNextChunk(
        PlacedChunkRecord current, IReadOnlyList<ChunkPoolMember> pool,
        Dictionary<(int, int), PlacedChunkRecord> grid, Random rng,
        bool requiredBoss, bool requiredForward, out PlacedChunkRecord? placed)
    {
        placed = null;
        ushort rotatedExits = ExitMask.Rotate(current.Template.Exits, current.Rotation);
        var sides = Enum.GetValues<ExitSide>().OrderBy(_ => rng.Next()).ToArray();

        foreach (var side in sides)
        {
            for (byte slot = 0; slot < 3; slot++)
            {
                if (!ExitMask.Has(rotatedExits, side, (ExitSlot)slot)) continue;
                if (current.StitchedMaskGet(side, slot)) continue;
                var (dx, dz) = ExitMask.GridDir(side);
                int nx = current.GridX + dx, nz = current.GridZ + dz;
                if (grid.ContainsKey((nx, nz))) continue;

                var neededSide = ExitMask.Opposite(side);
                var candidates = new List<(ChunkPoolMember member, byte rotation)>();
                foreach (var m in pool)
                {
                    if (requiredBoss && !m.Template.SpawnSlots.Any(s => s.Tag.Equals("boss", StringComparison.OrdinalIgnoreCase))) continue;
                    if (requiredForward && !m.Template.PortalSlots.Any(p => p.Role == PortalRole.Forward)) continue;
                    for (byte r = 0; r < 4; r++)
                    {
                        ushort rot = ExitMask.Rotate(m.Template.Exits, r);
                        if (ExitMask.Has(rot, neededSide, (ExitSlot)slot))
                            candidates.Add((m, r));
                    }
                }
                if (candidates.Count == 0) continue;

                var choice = candidates[rng.Next(candidates.Count)];
                placed = new PlacedChunkRecord(choice.member.Template, nx, nz, choice.rotation);
                current.StitchedMaskSet(side, slot);
                placed.StitchedMaskSet(neededSide, slot);
                return true;
            }
        }
        return false;
    }

    private static IReadOnlyList<PortalPlacement> BuildPortals(
        PlacedChunk entry, ChunkTemplate entryT, PlacedChunk? boss, ChunkTemplate? bossT, ProceduralMapConfig cfg)
    {
        var list = new List<PortalPlacement>();
        var backSlot = entryT.PortalSlots.First(p => p.Role == PortalRole.Back);
        var backWorld = TransformLocal(backSlot.LocalX, backSlot.LocalY, backSlot.LocalZ, entry.WorldPos, entry.Rotation);
        list.Add(new PortalPlacement(PortalRole.Back, backWorld, cfg.BackPortalTargetMapId));

        if (cfg.ForwardPortalTargetMapId is ushort fwd && boss is not null && bossT is not null)
        {
            var fSlot = bossT.PortalSlots.First(p => p.Role == PortalRole.Forward);
            var fWorld = TransformLocal(fSlot.LocalX, fSlot.LocalY, fSlot.LocalZ, boss.WorldPos, boss.Rotation);
            list.Add(new PortalPlacement(PortalRole.Forward, fWorld, fwd));
        }
        return list;
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

    private static ChunkPoolMember WeightedPick(IList<ChunkPoolMember> items, Random rng)
    {
        float total = items.Sum(i => i.Weight);
        float r = (float)(rng.NextDouble() * total);
        foreach (var i in items)
        {
            r -= i.Weight;
            if (r <= 0) return i;
        }
        return items[^1];
    }

    private sealed class PlacedChunkRecord
    {
        public ChunkTemplate Template { get; }
        public int GridX { get; }
        public int GridZ { get; }
        public byte Rotation { get; }
        private ushort _stitched;
        public PlacedChunkRecord(ChunkTemplate t, int gx, int gz, byte rotation)
        { Template = t; GridX = gx; GridZ = gz; Rotation = rotation; }
        public bool StitchedMaskGet(ExitSide side, byte slot) =>
            (_stitched & (1 << ((int)side * 3 + slot))) != 0;
        public void StitchedMaskSet(ExitSide side, byte slot) =>
            _stitched |= (ushort)(1 << ((int)side * 3 + slot));
    }
}
