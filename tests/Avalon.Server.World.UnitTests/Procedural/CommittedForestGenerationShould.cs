using Avalon.Common.ValueObjects;
using Avalon.Database.World;
using Avalon.Database.World.Seeding;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Handlers;
using Avalon.World.ChunkLayouts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Procedural;

/// <summary>
/// The shipped forest (forest content pass): the committed Maps catalog (chunks, pools, groups, spawn tables and map 2's
/// procedural config) seeded into SQLite exactly as the World server seeds it at start-up, and generated as the World
/// server generates it. A seed whose walk fails makes the forest unenterable for that player.
/// </summary>
public class CommittedForestGenerationShould
{
    private static readonly Lazy<(ProceduralMapConfig Config, List<ChunkPoolMember> Pool, List<ChunkGroupDefinition> Groups)> s_forest =
        new(Load, isThreadSafe: true);

    /// <summary>The forest's committed scenery pool (Maps/Scenery/2.json), read as the World server's catalog reads it.</summary>
    private static readonly Lazy<SceneryPool> s_scenery = new(() =>
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Maps", "Scenery", "2.json");
        return SceneryFile.Read(File.ReadAllText(path), path).Pool ?? throw new InvalidDataException($"{path} holds no pool");
    }, isThreadSafe: true);

    private static ProceduralMapConfig ForestConfig() => s_forest.Value.Config;

    private static (ProceduralMapConfig, List<ChunkPoolMember>, List<ChunkGroupDefinition>) Load()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext db = database.CreateDbContext();
        ChunkCatalogSeeder.SeedAsync(db, Path.Combine(AppContext.BaseDirectory, "Maps")).GetAwaiter().GetResult();

        var templates = db.ChunkTemplates.AsNoTracking().ToList().ToDictionary(t => t.Id);
        ChunkPool pool = db.ChunkPools.AsNoTracking().Include(p => p.Memberships).Include(p => p.Groups).ThenInclude(g => g.Members)
            .Single(p => p.Name == "forest_pool");

        ProceduralMapConfig config = db.ProceduralMapConfigs.AsNoTracking().Single(c => c.MapTemplateId == new MapTemplateId(2));

        return (config,
            pool.Memberships.OrderBy(m => m.ChunkTemplateId.Value).Select(m => new ChunkPoolMember(templates[m.ChunkTemplateId], m.Weight, m.MaxPerLayout)).ToList(),
            pool.Groups.Select(g => ChunkGroupDefinition.From(g, templates)!).ToList());
    }

    [Fact]
    public void Seed_the_forests_run_length_branching_and_bands_from_its_file()
    {
        ProceduralMapConfig config = ForestConfig();

        Assert.Equal(((ushort)12, (ushort)16, 0.5f, (byte)3), (config.MainPathMin, config.MainPathMax, config.BranchChance, config.BranchMaxDepth));
        Assert.Equal(new (int, int?, ushort, ushort)[] { (1, 3, 1, 3), (4, 7, 3, 6), (8, null, 5, 8) },
            config.DepthBands.OrderBy(b => b.MinDepth).Select(b => (b.MinDepth, b.MaxDepth, b.MinLevel, b.MaxLevel)));
    }

    /// <summary>Review Focus 1.</summary>
    [Fact]
    public void Generate_every_seed_of_the_committed_forest_without_failing()
    {
        (_, List<ChunkPoolMember> pool, List<ChunkGroupDefinition> groups) = s_forest.Value;
        var generator = new ProceduralLayoutGenerator(NullLoggerFactory.Instance);
        var rotations = new Dictionary<string, HashSet<byte>>(StringComparer.Ordinal);

        for (int seed = 0; seed < 1000; seed++)
        {
            ChunkLayout layout = generator.Generate(ForestConfig(), pool, seed * 7919, groups);

            Assert.InRange(layout.MainPathLength, 12, 16);
            Assert.Equal("forest_arena", layout.BossChunk?.Group);
            Assert.Equal(0, layout.EntryChunk.Depth);

            foreach (IGrouping<string, PlacedChunk> placed in layout.Chunks.Where(c => c.Group is not null).GroupBy(c => c.Group!))
            {
                Assert.Equal(4, placed.Count());   // intact, and each set piece at most once
                Assert.Single(placed.Select(c => c.Rotation).Distinct());
                Assert.Equal(1, placed.Max(c => c.GridX) - placed.Min(c => c.GridX));
                Assert.Equal(1, placed.Max(c => c.GridZ) - placed.Min(c => c.GridZ));
                if (!rotations.TryGetValue(placed.Key, out HashSet<byte>? seen)) rotations[placed.Key] = seen = [];
                seen.Add(placed.First().Rotation);
            }

            Assert.Equal(layout.Chunks.Count, layout.Chunks.Select(c => (c.GridX, c.GridZ)).Distinct().Count());
        }

        Assert.All(rotations.Values, seen => Assert.Equal(4, seen.Count));
    }

    /// <summary>
    /// #1113: a seed whose first attempt fails is retried with another seed, and that retry must not be the next seed's
    /// own first attempt, or the two seeds share one forest (layout, creatures and doodads, all keyed to the layout's
    /// seed).
    /// </summary>
    [Fact]
    public void Give_consecutive_seeds_different_layouts()
    {
        (ProceduralMapConfig config, List<ChunkPoolMember> pool, List<ChunkGroupDefinition> groups) = s_forest.Value;
        var generator = new ProceduralLayoutGenerator(NullLoggerFactory.Instance);
        var duplicates = new List<int>();
        string? previous = null;

        for (int seed = 0; seed < 200; seed++)
        {
            ChunkLayout layout = generator.Generate(config, pool, seed, groups);
            string fingerprint = string.Join(';', layout.Chunks.OrderBy(c => c.GridX).ThenBy(c => c.GridZ)
                .Select(c => $"{c.TemplateId.Value}@{c.GridX},{c.GridZ}r{c.Rotation}"));
            if (fingerprint == previous) duplicates.Add(seed);
            previous = fingerprint;
        }

        Assert.True(duplicates.Count == 0, $"seeds with the previous seed's layout: {string.Join(", ", duplicates)}");
    }

    /// <summary>
    /// Owner decision: a set piece other than the boss arena stands no earlier than main-path step 8, where the 5-8 band
    /// begins, so a party leaving the entry does not meet level 5-8 packs in the next cells. Every step before the first
    /// set piece is one chunk, so the step a set piece was placed at is its nearest member's depth, and any later set
    /// piece comes at a later step.
    /// </summary>
    [Fact]
    public void Place_no_set_piece_but_the_arena_before_main_path_step_eight()
    {
        (ProceduralMapConfig config, List<ChunkPoolMember> pool, List<ChunkGroupDefinition> groups) = s_forest.Value;
        Assert.Equal(8, config.MinSetPieceStep);
        var generator = new ProceduralLayoutGenerator(NullLoggerFactory.Instance);
        int placed = 0;

        for (int seed = 0; seed < 1000; seed++)
        {
            ChunkLayout layout = generator.Generate(config, pool, seed * 7919, groups);
            foreach (IGrouping<string, PlacedChunk> piece in layout.Chunks
                         .Where(c => c.Group is not null && c.Group != "forest_arena").GroupBy(c => c.Group!))
            {
                placed++;
                Assert.True(piece.Min(c => c.Depth) >= 8, $"seed {seed * 7919}: {piece.Key} starts at step {piece.Min(c => c.Depth)}");
            }
        }

        Assert.True(placed > 550, $"only {placed} set pieces were placed in 1000 runs");   // measured 668 (661 before #1113, 690 before #1109's fifteen singles)
    }

    /// <summary>
    /// Review Focus 4, overhangs everywhere or never (owner: one, at most two, chunks of a run carry an overhang): the
    /// pieces tagged <c>overhang</c> are capped in the pool, so no layout holds more than two, and weighted, so most hold one.
    /// </summary>
    [Fact]
    public void Place_at_most_two_overhang_chunks_in_a_layout_and_one_in_most()
    {
        (ProceduralMapConfig config, List<ChunkPoolMember> pool, List<ChunkGroupDefinition> groups) = s_forest.Value;
        var overhangs = pool.Where(m => m.Template.Tags.Contains("overhang")).Select(m => m.Template.Id).ToHashSet();
        Assert.NotEmpty(overhangs);   // a pool with no overhang piece would pass the cap below
        var generator = new ProceduralLayoutGenerator(NullLoggerFactory.Instance);
        int[] layouts = new int[3];

        for (int seed = 0; seed < 1000; seed++)
        {
            ChunkLayout layout = generator.Generate(config, pool, seed * 7919, groups);
            int count = layout.Chunks.Count(c => overhangs.Contains(c.TemplateId));
            Assert.True(count <= 2, $"seed {seed * 7919}: {count} overhang chunks");
            layouts[count]++;
        }

        Assert.True(layouts[0] <= 200, $"{layouts[0]} of 1000 layouts hold no overhang (one: {layouts[1]}, two: {layouts[2]})");
    }

    /// <summary>
    /// Review Focus 2 and 5 (#1110): every committed layout, ringed from the committed pool, gets one scenery piece on
    /// each ring cell and none on its own cells, a cap of the right shape at every cell an open exit faces and no cap
    /// elsewhere, the town-ward scene on the spots around the entry, no non-filler border beside itself, and the same
    /// ring every time from the same layout.
    /// </summary>
    [Fact]
    public void Ring_every_committed_layout_and_cap_every_open_exit()
    {
        (ProceduralMapConfig config, List<ChunkPoolMember> chunkPool, List<ChunkGroupDefinition> groups) = s_forest.Value;
        var exits = chunkPool.Select(m => m.Template)
            .Concat(groups.SelectMany(g => g.Cells.Select(c => c.Template)))
            .DistinctBy(t => t.Id)
            .ToDictionary(t => t.Id, t => t.Exits);
        ushort ExitsOf(ChunkTemplateId id) => exits[id];
        SceneryPool pool = s_scenery.Value;
        var pieces = pool.Pieces.ToDictionary(p => p.Name, StringComparer.Ordinal);
        var generator = new ProceduralLayoutGenerator(NullLoggerFactory.Instance);
        int caps = 0;
        int spots = 0;

        for (int seed = 0; seed < 1000; seed++)
        {
            ChunkLayout layout = generator.Generate(config, chunkPool, seed * 7919, groups);
            var cells = layout.Chunks.Select(c => ((int)c.GridX, (int)c.GridZ)).ToHashSet();
            IReadOnlyList<SceneryPlacement> ring = SceneryRing.Build(layout, ExitsOf, pool);
            var at = ring.ToDictionary(p => (p.GridX, p.GridZ));

            Assert.Equal(SceneryRing.Cells(cells), ring.Select(p => (p.GridX, p.GridZ)));

            IReadOnlyDictionary<(int X, int Z), byte> capped = SceneryRing.CappedSides(layout, ExitsOf);
            foreach (((int X, int Z) cell, byte mask) in capped)
            {
                SceneryPoolPiece cap = pieces[at[cell].Name];
                Assert.Equal(SceneryKind.Cap, cap.Kind);
                Assert.Equal(mask, ScenerySides.Rotate(cap.CappedSides, at[cell].Rotation));
            }

            caps += capped.Count;
            foreach (SceneryPlacement placement in ring)
            {
                (int X, int Z) cell = (placement.GridX, placement.GridZ);
                SceneryPoolPiece piece = pieces[placement.Name];
                if (!capped.ContainsKey(cell))
                    Assert.NotEqual(SceneryKind.Cap, piece.Kind);

                // A border beside the layout turns its front to the first of N, E, S, W that is a layout cell.
                ExitSide[] beside = [.. s_sides.Where(side => cells.Contains((cell.X + ExitMask.GridDir(side).dx, cell.Z + ExitMask.GridDir(side).dz)))];
                if (piece.Kind == SceneryKind.Border && beside.Length > 0)
                {
                    string front = ScenerySides.Name(ScenerySides.Rotate(ScenerySides.Bit(piece.Front), placement.Rotation));
                    Assert.True(front == beside[0].ToString(), $"seed {seed * 7919}: the border at {cell} faces {front}, not {beside[0]}");
                }

                if (piece.Kind == SceneryKind.Border && !piece.Filler)
                {
                    Assert.False(at.TryGetValue((cell.X + 1, cell.Z), out SceneryPlacement? east) && east.Name == piece.Name,
                        $"seed {seed * 7919}: {piece.Name} at {cell} and east of it");
                    Assert.False(at.TryGetValue((cell.X, cell.Z + 1), out SceneryPlacement? north) && north.Name == piece.Name,
                        $"seed {seed * 7919}: {piece.Name} at {cell} and north of it");
                }
            }

            foreach (SceneryPoolPiece spot in pool.Pieces.Where(p => p.Kind == SceneryKind.TownWard))
            {
                (int X, int Z) cell = (layout.EntryChunk.GridX + spot.SpotX, layout.EntryChunk.GridZ + spot.SpotZ);
                if (!cells.Contains(cell) && !capped.ContainsKey(cell))
                {
                    Assert.Equal(new SceneryPlacement(spot.Name, cell.X, cell.Z, 0), at[cell]);
                    spots++;
                }
            }

            Assert.Equal(ring, SceneryRing.Build(layout, ExitsOf, pool));
        }

        Assert.True(caps > 5000, $"only {caps} capped cells in 1000 layouts");   // measured 8977
        Assert.True(spots > 3000, $"only {spots} town-ward spots held their own piece in 1000 layouts");   // measured 4553
    }

    private static readonly ExitSide[] s_sides = [ExitSide.N, ExitSide.E, ExitSide.S, ExitSide.W];

    /// <summary>
    /// #1110: the scenery ring is drawn, never walked, so the navmesh bake must not see it: a layout bakes from the same
    /// combined obj whether or not it carries its ring (from the committed pool).
    /// </summary>
    [Fact]
    public void Compose_one_bake_input_whether_or_not_the_layout_carries_its_ring()
    {
        (ProceduralMapConfig config, List<ChunkPoolMember> chunkPool, List<ChunkGroupDefinition> groups) = s_forest.Value;
        var templates = chunkPool.Select(m => m.Template)
            .Concat(groups.SelectMany(g => g.Cells.Select(c => c.Template)))
            .DistinctBy(t => t.Id)
            .ToDictionary(t => t.Id);
        IChunkLibrary library = Substitute.For<IChunkLibrary>();
        library.GetById(Arg.Any<ChunkTemplateId>()).Returns(call => templates[call.Arg<ChunkTemplateId>()]);
        ChunkLayout layout = new ProceduralLayoutGenerator(NullLoggerFactory.Instance).Generate(config, chunkPool, 71, groups);
        IReadOnlyList<SceneryPlacement> ring = SceneryRing.Build(layout, id => templates[id].Exits, s_scenery.Value);
        var builder = ChunkLayoutNavmeshBuilder.ForTesting(NullLoggerFactory.Instance, library, AppContext.BaseDirectory);

        Assert.NotEmpty(ring);
        Assert.Equal(builder.ComposeCombinedObj(layout), builder.ComposeCombinedObj(layout with { Scenery = ring }));
    }

    [Fact]
    public void Reach_the_boss_arena_deeper_than_the_first_band()
    {
        (_, List<ChunkPoolMember> pool, List<ChunkGroupDefinition> groups) = s_forest.Value;
        var generator = new ProceduralLayoutGenerator(NullLoggerFactory.Instance);

        for (int seed = 0; seed < 200; seed++)
        {
            ChunkLayout layout = generator.Generate(ForestConfig(), pool, seed, groups);
            Assert.True(layout.BossChunk!.Depth >= 9, $"seed {seed}: the boss is only {layout.BossChunk.Depth} steps in");
        }
    }
}
