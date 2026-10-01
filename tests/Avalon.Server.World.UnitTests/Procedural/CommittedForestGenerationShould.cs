using Avalon.Common.ValueObjects;
using Avalon.Database.World;
using Avalon.Database.World.Seeding;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Handlers;
using Avalon.World.ChunkLayouts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Avalon.Server.World.UnitTests.Procedural;

/// <summary>
/// The shipped forest (forest content pass): the committed Maps catalog (chunks, pools, groups, spawn tables and map 2's
/// procedural config) seeded into SQLite exactly as the World server seeds it at start-up, and generated as the World
/// server generates it. A seed whose walk fails makes the forest unenterable for that player.
/// </summary>
public class CommittedForestGenerationShould
{
    private static readonly Lazy<(ProceduralMapConfig Config, List<ChunkPoolMember> Pool, List<ChunkGroupDefinition> Groups)> Forest =
        new(Load, isThreadSafe: true);

    private static ProceduralMapConfig ForestConfig() => Forest.Value.Config;

    private static (ProceduralMapConfig, List<ChunkPoolMember>, List<ChunkGroupDefinition>) Load()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext db = database.CreateDbContext();
        ChunkCatalogSeeder.SeedAsync(db, Path.Combine(AppContext.BaseDirectory, "Maps")).GetAwaiter().GetResult();

        Dictionary<ChunkTemplateId, ChunkTemplate> templates = db.ChunkTemplates.AsNoTracking().ToList().ToDictionary(t => t.Id);
        ChunkPool pool = db.ChunkPools.AsNoTracking().Include(p => p.Memberships).Include(p => p.Groups).ThenInclude(g => g.Members)
            .Single(p => p.Name == "forest_pool");

        ProceduralMapConfig config = db.ProceduralMapConfigs.AsNoTracking().Single(c => c.MapTemplateId == new MapTemplateId(2));

        return (config,
            pool.Memberships.Select(m => new ChunkPoolMember(templates[m.ChunkTemplateId], m.Weight)).ToList(),
            pool.Groups.Select(g => ChunkGroupDefinition.From(g, templates)!).ToList());
    }

    [Fact]
    public void Seed_the_forests_run_length_branching_and_bands_from_its_file()
    {
        ProceduralMapConfig config = ForestConfig();

        Assert.Equal(((ushort)10, (ushort)16, 0.5f, (byte)3), (config.MainPathMin, config.MainPathMax, config.BranchChance, config.BranchMaxDepth));
        Assert.Equal(new (int, int?, ushort, ushort)[] { (1, 3, 1, 3), (4, 7, 3, 6), (8, null, 5, 8) },
            config.DepthBands.OrderBy(b => b.MinDepth).Select(b => (b.MinDepth, b.MaxDepth, b.MinLevel, b.MaxLevel)));
    }

    /// <summary>Review Focus 1.</summary>
    [Fact]
    public void Generate_every_seed_of_the_committed_forest_without_failing()
    {
        (_, List<ChunkPoolMember> pool, List<ChunkGroupDefinition> groups) = Forest.Value;
        var generator = new ProceduralLayoutGenerator(NullLoggerFactory.Instance);
        var rotations = new Dictionary<string, HashSet<byte>>(StringComparer.Ordinal);

        for (int seed = 0; seed < 1000; seed++)
        {
            ChunkLayout layout = generator.Generate(ForestConfig(), pool, seed * 7919, groups);

            Assert.InRange(layout.MainPathLength, 10, 16);
            Assert.Equal("forest_arena", layout.BossChunk?.Group);
            Assert.Equal(0, layout.EntryChunk.Depth);

            foreach (IGrouping<string, PlacedChunk> placed in layout.Chunks.Where(c => c.Group is not null).GroupBy(c => c.Group!))
            {
                Assert.Equal(4, placed.Count());   // intact, and each set piece at most once
                Assert.Single(placed.Select(c => c.Rotation).Distinct());
                Assert.Equal(1, placed.Max(c => c.GridX) - placed.Min(c => c.GridX));
                Assert.Equal(1, placed.Max(c => c.GridZ) - placed.Min(c => c.GridZ));
                if (!rotations.TryGetValue(placed.Key, out var seen)) rotations[placed.Key] = seen = [];
                seen.Add(placed.First().Rotation);
            }

            Assert.Equal(layout.Chunks.Count, layout.Chunks.Select(c => (c.GridX, c.GridZ)).Distinct().Count());
        }

        Assert.All(rotations.Values, seen => Assert.Equal(4, seen.Count));
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
        (ProceduralMapConfig config, List<ChunkPoolMember> pool, List<ChunkGroupDefinition> groups) = Forest.Value;
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

        Assert.True(placed > 400, $"only {placed} set pieces were placed in 1000 runs");   // measured 545
    }

    [Fact]
    public void Reach_the_boss_arena_deeper_than_the_first_band()
    {
        (_, List<ChunkPoolMember> pool, List<ChunkGroupDefinition> groups) = Forest.Value;
        var generator = new ProceduralLayoutGenerator(NullLoggerFactory.Instance);

        for (int seed = 0; seed < 200; seed++)
        {
            ChunkLayout layout = generator.Generate(ForestConfig(), pool, seed, groups);
            Assert.True(layout.BossChunk!.Depth >= 9, $"seed {seed}: the boss is only {layout.BossChunk.Depth} steps in");
        }
    }
}
