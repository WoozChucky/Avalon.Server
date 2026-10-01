using Avalon.Common.Mathematics;
using Avalon.Domain.World;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.World.ChunkLayouts;

/// <summary>
/// Pure deterministic procedural-layout generator. Inputs: <see cref="ProceduralMapConfig"/>, chunk pool, the pool's
/// set pieces (<see cref="ChunkGroupDefinition"/>, forest content pass) and a seed. Output: <see cref="ChunkLayout"/>.
/// No DB / DI / scope dependencies — safe for use from preview tooling (Api admin endpoints) and production world
/// bootstrap alike.
/// <para>
/// A main-path step places one node: a single chunk, or a whole set piece on as many free cells as it has members,
/// turned as a whole and joined to its parent only through an exit on one of its outer edges. A set piece is placed
/// only on the main path; a boss set piece only as its last step, every other one at most once per layout and not
/// before the config's <see cref="ProceduralMapConfig.MinSetPieceStep"/>. Each chunk records its depth: the grid steps
/// from the entry over stitched connections, a set piece's inner edges included.
/// A pool without set pieces draws exactly the random numbers it always did, so its layouts are unchanged.
/// </para>
/// </summary>
public class ProceduralLayoutGenerator
{
    private const int MaxRetries = 10;
    private readonly ILogger _logger;

    public ProceduralLayoutGenerator(ILoggerFactory? loggerFactory = null)
    {
        _logger = (loggerFactory ?? NullLoggerFactory.Instance)
            .CreateLogger<ProceduralLayoutGenerator>();
    }

    public ChunkLayout Generate(ProceduralMapConfig config, IReadOnlyList<ChunkPoolMember> pool, int seed,
        IReadOnlyList<ChunkGroupDefinition>? groups = null)
    {
        IReadOnlyList<ChunkGroupDefinition> sets = groups ?? [];
        string? error = null;
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            int attemptSeed = seed + attempt;
            if (TryGenerate(config, pool, sets, attemptSeed, out var layout, out error))
                return layout!;
            // A retried attempt is routine (many forest builds need one); only the last failure is worth a warning.
            if (attempt + 1 < MaxRetries)
                _logger.LogDebug("Procedural generation attempt {Attempt} failed: {Error}", attempt + 1, error);
        }

        _logger.LogWarning("Procedural generation of map {MapId} failed all {Attempts} attempts from seed {Seed}; the last: {Error}",
            config.MapTemplateId.Value, MaxRetries, seed, error);
        throw new ProceduralGenerationFailedException(
            $"Failed to generate layout for map {config.MapTemplateId.Value} after {MaxRetries} attempts");
    }

    private bool TryGenerate(ProceduralMapConfig cfg, IReadOnlyList<ChunkPoolMember> pool,
        IReadOnlyList<ChunkGroupDefinition> groups, int seed, out ChunkLayout? layout, out string? error)
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
        var entryRecord = new PlacedChunkRecord(entryMember.Template, 0, 0, 0, group: null);
        var mainPath = new List<Node> { new([entryRecord]) };
        var grid = new Dictionary<(int, int), PlacedChunkRecord> { [(0, 0)] = entryRecord };
        var links = new List<((int, int) A, (int, int) B)>();
        var usedGroups = new HashSet<string>(StringComparer.Ordinal);

        // 2. Walk main path
        for (int step = 1; step < pathLen; step++)
        {
            bool requiredForward = step == pathLen - 1 && cfg.ForwardPortalTargetMapId is not null;
            bool requiredBoss    = step == pathLen - 1 && cfg.HasBoss;
            // Mid-path chunks must have ≥2 exits — single-exit chunks (deadends, boss with
            // S-only) trap the walk on their second tick (only exit is the one we entered
            // through). Last step is exempt: boss has 1 exit by design.
            bool excludeSingleExit = !requiredBoss && !requiredForward;

            if (!TryAttachNext(mainPath[^1], pool, groups, grid, links, usedGroups, rng,
                    new AttachRules(requiredBoss, requiredForward, excludeSingleExit, AllowGroups: true,
                        AllowSetPieces: step >= cfg.MinSetPieceStep), out var placed))
            {
                error = $"Could not attach at step {step}";
                return false;
            }
            mainPath.Add(placed!);
        }

        // 3. Branches (single chunks only)
        for (int i = 1; i < mainPath.Count - 1; i++)
        {
            if (rng.NextDouble() >= cfg.BranchChance) continue;
            int branchLen = rng.Next(1, cfg.BranchMaxDepth + 1);
            var tail = mainPath[i];
            for (int b = 0; b < branchLen; b++)
            {
                // Branches may end in deadends, so single-exit chunks are fair game.
                if (!TryAttachNext(tail, pool, groups, grid, links, usedGroups, rng,
                        new AttachRules(RequiredBoss: false, RequiredForward: false, ExcludeSingleExit: false, AllowGroups: false,
                            AllowSetPieces: false),
                        out var placed))
                    break;
                tail = placed!;
            }
        }

        // 4. Depth, then assemble
        Dictionary<(int, int), int> depths = Depths(links);
        float cellSize = entryMember.Template.CellSize;
        var placedChunks = grid.Values.Select(r => new PlacedChunk(
            r.Template.Id, (short)r.GridX, (short)r.GridZ, r.Rotation,
            new Vector3(r.GridX * cellSize, 0, r.GridZ * cellSize),
            depths.GetValueOrDefault((r.GridX, r.GridZ)), r.Group)).ToList();

        var entry = placedChunks.First(p => p.GridX == 0 && p.GridZ == 0);
        Node last = mainPath[^1];
        PlacedChunkRecord? bossRec = cfg.HasBoss ? last.Members.FirstOrDefault(m => HasTag(m.Template, "boss")) ?? last.Members[0] : null;
        var boss = bossRec is null ? null : placedChunks.First(p => p.GridX == bossRec.GridX && p.GridZ == bossRec.GridZ);
        PlacedChunkRecord? forwardRec = bossRec is null
            ? null
            : last.Members.FirstOrDefault(m => m.Template.PortalSlots.Any(p => p.Role == PortalRole.Forward)) ?? bossRec;
        var forward = forwardRec is null ? null : placedChunks.First(p => p.GridX == forwardRec.GridX && p.GridZ == forwardRec.GridZ);

        var entrySlot = entryMember.Template.SpawnSlots.First(s => s.Tag.Equals("entry", StringComparison.OrdinalIgnoreCase));
        var entrySpawnWorldPos = ChunkRotation.LocalToWorld(entrySlot.LocalX, entrySlot.LocalY, entrySlot.LocalZ, entry.Rotation, cellSize, entry.WorldPos);

        var portals = BuildPortals(entry, entryMember.Template, forward, forwardRec?.Template, cfg, cellSize);

        layout = new ChunkLayout(seed, placedChunks, entry, boss, portals, entrySpawnWorldPos, cellSize,
            Config: cfg, ConfigVersion: LayoutConfigVersion.Compute(cfg, pool, groups), MainPathLength: mainPath.Count);
        return true;
    }

    /// <summary>
    /// AllowGroups: set pieces may be placed at all (the main path); AllowSetPieces: so may one other than the boss's (the map's
    /// MinSetPieceStep is reached).
    /// </summary>
    private readonly record struct AttachRules(bool RequiredBoss, bool RequiredForward, bool ExcludeSingleExit, bool AllowGroups,
        bool AllowSetPieces);

    /// <summary>A placement candidate: a single chunk at a rotation, or a set piece at a rotation with one member (the anchor) on the free cell.</summary>
    private readonly record struct Candidate(
        ChunkTemplate? Template, byte Rotation, ChunkGroupDefinition? Group, ChunkGroupCell? Anchor, int OriginX, int OriginZ);

    private static bool TryAttachNext(
        Node current, IReadOnlyList<ChunkPoolMember> pool, IReadOnlyList<ChunkGroupDefinition> groups,
        Dictionary<(int, int), PlacedChunkRecord> grid, List<((int, int) A, (int, int) B)> links, HashSet<string> usedGroups,
        Random rng, AttachRules rules, out Node? placed)
    {
        placed = null;
        // A single chunk draws nothing here, so a pool without set pieces generates what it always did.
        IReadOnlyList<PlacedChunkRecord> members = current.Members.Count == 1
            ? current.Members
            : current.Members.OrderBy(_ => rng.Next()).ToArray();

        foreach (PlacedChunkRecord from in members)
        {
            ushort rotatedExits = ExitMask.Rotate(from.Template.Exits, from.Rotation);
            var sides = Enum.GetValues<ExitSide>().OrderBy(_ => rng.Next()).ToArray();

            foreach (var side in sides)
            {
                for (byte slot = 0; slot < 3; slot++)
                {
                    if (!ExitMask.Has(rotatedExits, side, (ExitSlot)slot)) continue;
                    if (from.StitchedMaskGet(side, slot)) continue;
                    var (dx, dz) = ExitMask.GridDir(side);
                    int nx = from.GridX + dx, nz = from.GridZ + dz;
                    // An exit facing another member of the same set piece faces an occupied cell, so it is skipped here too.
                    if (grid.ContainsKey((nx, nz))) continue;

                    var neededSide = ExitMask.Opposite(side);
                    var candidates = new List<Candidate>();
                    AddChunkCandidates(pool, rules, neededSide, slot, candidates);

                    if (rules.AllowGroups)
                        AddGroupCandidates(groups, grid, usedGroups, rules, nx, nz, neededSide, slot, candidates);

                    if (candidates.Count == 0) continue;

                    var choice = candidates[rng.Next(candidates.Count)];
                    PlacedChunkRecord anchor;
                    if (choice.Group is null)
                    {
                        anchor = new PlacedChunkRecord(choice.Template!, nx, nz, choice.Rotation, group: null);
                        grid[(nx, nz)] = anchor;
                        placed = new Node([anchor]);
                    }
                    else
                    {
                        placed = PlaceGroup(choice, grid, links, out anchor);
                        usedGroups.Add(choice.Group.Name);
                    }

                    from.StitchedMaskSet(side, slot);
                    anchor.StitchedMaskSet(neededSide, slot);
                    links.Add(((from.GridX, from.GridZ), (nx, nz)));
                    return true;
                }
            }
        }
        return false;
    }

    private static void AddChunkCandidates(IReadOnlyList<ChunkPoolMember> pool, AttachRules rules, ExitSide neededSide, byte slot,
        List<Candidate> candidates)
    {
        foreach (var m in pool)
        {
            if (rules.RequiredBoss && !HasTag(m.Template, "boss")) continue;
            if (rules.RequiredForward && !m.Template.PortalSlots.Any(p => p.Role == PortalRole.Forward)) continue;
            if (rules.ExcludeSingleExit && CountExits(m.Template.Exits) <= 1) continue;
            for (byte r = 0; r < 4; r++)
            {
                ushort rot = ExitMask.Rotate(m.Template.Exits, r);
                if (ExitMask.Has(rot, neededSide, (ExitSlot)slot))
                    candidates.Add(new Candidate(m.Template, r, null, null, 0, 0));
            }
        }
    }

    private static void AddGroupCandidates(IReadOnlyList<ChunkGroupDefinition> groups, Dictionary<(int, int), PlacedChunkRecord> grid,
        HashSet<string> usedGroups, AttachRules rules, int nx, int nz, ExitSide neededSide, byte slot, List<Candidate> candidates)
    {
        foreach (ChunkGroupDefinition group in groups)
        {
            // A boss set piece only ends the main path, and nothing else ends it while one is required.
            if (group.IsBoss != rules.RequiredBoss) continue;
            if (!group.IsBoss && (!rules.AllowSetPieces || usedGroups.Contains(group.Name))) continue;
            if (rules.RequiredForward && !group.HasForward) continue;
            if (rules.ExcludeSingleExit && group.OuterExitCount < 2) continue;

            for (byte r = 0; r < 4; r++)
            {
                foreach (ChunkGroupCell cell in group.Cells)
                {
                    if (!ExitMask.Has(ExitMask.Rotate(cell.Template.Exits, r), neededSide, (ExitSlot)slot)) continue;
                    (int cx, int cz) = ChunkGroupRotation.RotateCell(cell.CellX, cell.CellZ, group.SizeX, group.SizeZ, r);
                    int ox = nx - cx, oz = nz - cz;
                    bool free = group.Cells.All(c =>
                    {
                        (int x, int z) = ChunkGroupRotation.RotateCell(c.CellX, c.CellZ, group.SizeX, group.SizeZ, r);
                        return !grid.ContainsKey((ox + x, oz + z));
                    });
                    if (free)
                        candidates.Add(new Candidate(null, r, group, cell, ox, oz));
                }
            }
        }
    }

    private static Node PlaceGroup(Candidate choice, Dictionary<(int, int), PlacedChunkRecord> grid,
        List<((int, int) A, (int, int) B)> links, out PlacedChunkRecord anchor)
    {
        ChunkGroupDefinition group = choice.Group!;
        var records = new List<PlacedChunkRecord>(group.Cells.Count);
        PlacedChunkRecord? found = null;
        foreach (ChunkGroupCell cell in group.Cells)
        {
            (int x, int z) = ChunkGroupRotation.RotateCell(cell.CellX, cell.CellZ, group.SizeX, group.SizeZ, choice.Rotation);
            var record = new PlacedChunkRecord(cell.Template, choice.OriginX + x, choice.OriginZ + z, choice.Rotation, group.Name);
            grid[(record.GridX, record.GridZ)] = record;
            records.Add(record);
            if (ReferenceEquals(cell, choice.Anchor)) found = record;
        }

        // The anchor is one of the group's own cells (AddGroupCandidates), so this only fails on a programming error.
        anchor = found ?? throw new InvalidOperationException($"Set piece '{group.Name}' was placed without its anchor cell.");

        // The inner edges are open ground: they count as connections for depth.
        foreach (PlacedChunkRecord a in records)
            foreach (PlacedChunkRecord b in records)
                if ((a.GridX + 1 == b.GridX && a.GridZ == b.GridZ) || (a.GridX == b.GridX && a.GridZ + 1 == b.GridZ))
                    links.Add(((a.GridX, a.GridZ), (b.GridX, b.GridZ)));

        return new Node(records);
    }

    /// <summary>Grid steps from the entry over the stitched connections (breadth-first).</summary>
    private static Dictionary<(int, int), int> Depths(List<((int, int) A, (int, int) B)> links)
    {
        var neighbours = new Dictionary<(int, int), List<(int, int)>>();
        foreach (((int, int) a, (int, int) b) in links)
        {
            if (!neighbours.TryGetValue(a, out var la)) neighbours[a] = la = [];
            if (!neighbours.TryGetValue(b, out var lb)) neighbours[b] = lb = [];
            la.Add(b);
            lb.Add(a);
        }

        var depth = new Dictionary<(int, int), int> { [(0, 0)] = 0 };
        var queue = new Queue<(int, int)>();
        queue.Enqueue((0, 0));
        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            if (!neighbours.TryGetValue(cell, out var next)) continue;
            foreach (var n in next)
                if (depth.TryAdd(n, depth[cell] + 1))
                    queue.Enqueue(n);
        }
        return depth;
    }

    private static bool HasTag(ChunkTemplate template, string tag) =>
        template.SpawnSlots.Any(s => s.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase));

    private static int CountExits(ushort mask)
    {
        int n = 0;
        for (int i = 0; i < 12; i++) if ((mask & (1 << i)) != 0) n++;
        return n;
    }

    private static IReadOnlyList<PortalPlacement> BuildPortals(
        PlacedChunk entry, ChunkTemplate entryT, PlacedChunk? forward, ChunkTemplate? forwardT, ProceduralMapConfig cfg, float cellSize)
    {
        var list = new List<PortalPlacement>();
        var backSlot = entryT.PortalSlots.First(p => p.Role == PortalRole.Back);
        var backWorld = ChunkRotation.LocalToWorld(backSlot.LocalX, backSlot.LocalY, backSlot.LocalZ, entry.Rotation, cellSize, entry.WorldPos);
        list.Add(new PortalPlacement(PortalRole.Back, backWorld, cfg.BackPortalTargetMapId));

        if (cfg.ForwardPortalTargetMapId is ushort fwd && forward is not null && forwardT is not null)
        {
            var fSlot = forwardT.PortalSlots.First(p => p.Role == PortalRole.Forward);
            var fWorld = ChunkRotation.LocalToWorld(fSlot.LocalX, fSlot.LocalY, fSlot.LocalZ, forward.Rotation, cellSize, forward.WorldPos);
            list.Add(new PortalPlacement(PortalRole.Forward, fWorld, fwd));
        }
        return list;
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

    /// <summary>One main-path or branch step: a single chunk or a whole set piece.</summary>
    private sealed class Node(IReadOnlyList<PlacedChunkRecord> members)
    {
        public IReadOnlyList<PlacedChunkRecord> Members { get; } = members;
    }

    private sealed class PlacedChunkRecord
    {
        public ChunkTemplate Template { get; }
        public int GridX { get; }
        public int GridZ { get; }
        public byte Rotation { get; }
        public string? Group { get; }
        private ushort _stitched;
        public PlacedChunkRecord(ChunkTemplate t, int gx, int gz, byte rotation, string? group)
        { Template = t; GridX = gx; GridZ = gz; Rotation = rotation; Group = group; }
        public bool StitchedMaskGet(ExitSide side, byte slot) =>
            (_stitched & (1 << ((int)side * 3 + slot))) != 0;
        public void StitchedMaskSet(ExitSide side, byte slot) =>
            _stitched |= (ushort)(1 << ((int)side * 3 + slot));
    }
}
