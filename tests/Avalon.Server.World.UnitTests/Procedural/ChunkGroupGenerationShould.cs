using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Creatures;
using Avalon.World.ChunkLayouts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Procedural;

/// <summary>
/// Forest content pass: a set piece is four 1x1 chunks placed at once on four free cells, turned as a whole, joined
/// only through exits on its outer edges; every chunk carries its depth (grid steps from the entry).
/// </summary>
public class ChunkGroupGenerationShould
{
    private const ushort N = 0b_0000_0000_0000_0010, E = 0b_0000_0000_0001_0000, S = 0b_0000_0000_1000_0000, W = 0b_0000_0100_0000_0000;

    private static ChunkTemplate Chunk(int id, ushort exits, string? tag = null, PortalRole? portal = null)
    {
        var t = new ChunkTemplate { Id = new ChunkTemplateId(id), Name = $"c{id}", GeometryFile = $"Chunks/c{id}.obj", CellSize = 30f, Exits = exits };
        if (tag is not null) t.SpawnSlots.Add(new ChunkSpawnSlot { Tag = tag, LocalX = 15, LocalY = 1, LocalZ = 15 });
        if (portal is { } role) t.PortalSlots.Add(new ChunkPortalSlot { Role = role, LocalX = 15, LocalY = 1, LocalZ = 5 });
        return t;
    }

    private static List<ChunkPoolMember> Pool() =>
    [
        new(Chunk(1, N, "entry", PortalRole.Back), 1f),
        new(Chunk(2, (ushort)(N | S)), 1f),
        new(Chunk(3, (ushort)(E | W)), 1f),
        new(Chunk(4, (ushort)(N | E)), 1f),
        new(Chunk(5, (ushort)(S | E)), 1f),
        new(Chunk(6, (ushort)(N | E | S | W)), 1f),
    ];

    /// <summary>A clearing with an exit on each side, one per member, and nothing on its inner edges.</summary>
    private static ChunkGroupDefinition Clearing() => new("clearing",
    [
        new ChunkGroupCell(Chunk(11, S), 0, 0), new ChunkGroupCell(Chunk(12, E), 1, 0),
        new ChunkGroupCell(Chunk(13, W), 0, 1), new ChunkGroupCell(Chunk(14, N), 1, 1),
    ]);

    /// <summary>A boss arena entered only from the south of its south-west member; the boss stands in the north-east.</summary>
    private static ChunkGroupDefinition Arena() => new("arena",
    [
        new ChunkGroupCell(Chunk(21, S), 0, 0), new ChunkGroupCell(Chunk(22, 0), 1, 0),
        new ChunkGroupCell(Chunk(23, 0), 0, 1), new ChunkGroupCell(Chunk(24, 0, "boss"), 1, 1),
    ]);

    private static ProceduralMapConfig Config(int min, int max, bool boss) => new()
    {
        MapTemplateId = new MapTemplateId(20),
        ChunkPoolId = new ChunkPoolId(1),
        SpawnTableId = new SpawnTableId(1),
        MainPathMin = (ushort)min,
        MainPathMax = (ushort)max,
        BranchChance = 0.5f,
        BranchMaxDepth = 2,
        HasBoss = boss,
        BackPortalTargetMapId = 1,
    };

    private static ProceduralLayoutGenerator Generator() => new(NullLoggerFactory.Instance);

    /// <summary>
    /// This small pool boxes its own walk in on about one seed in a hundred, with or without the clearing (every
    /// attempt's main path runs into occupied cells); such a seed says nothing about how a group is placed. The loops
    /// count those seeds and allow about twice the rate measured (2 of 200, 4 of 400), and require the clearing in most
    /// layouts (measured 180 of 200, 363 of 400), so neither can pass with nothing generated.
    /// </summary>
    private static ChunkLayout? TryGenerate(int seed)
    {
        try { return Generator().Generate(Config(8, 12, boss: false), Pool(), seed, [Clearing()]); }
        catch (ProceduralGenerationFailedException) { return null; }
    }

    [Fact]
    public void Place_every_group_intact_under_every_rotation()
    {
        var rotations = new HashSet<byte>();
        int failed = 0, placed = 0;
        for (int seed = 0; seed < 400; seed++)
        {
            if (TryGenerate(seed) is not { } layout) { failed++; continue; }
            List<PlacedChunk> members = layout.Chunks.Where(c => c.Group == "clearing").ToList();
            if (members.Count == 0) continue;
            placed++;

            Assert.Equal(4, members.Count);
            byte rotation = members[0].Rotation;
            Assert.All(members, m => Assert.Equal(rotation, m.Rotation));
            rotations.Add(rotation);

            int ox = members.Min(m => m.GridX), oz = members.Min(m => m.GridZ);
            foreach (ChunkGroupCell cell in Clearing().Cells)
            {
                (int x, int z) = ChunkGroupRotation.RotateCell(cell.CellX, cell.CellZ, 2, 2, rotation);
                Assert.Contains(members, m => m.GridX == ox + x && m.GridZ == oz + z && m.TemplateId == cell.Template.Id);
            }
        }

        Assert.Equal([0, 1, 2, 3], rotations.Order().Select(r => (int)r));
        Assert.InRange(failed, 0, 8);
        Assert.InRange(placed, 300, 400);
    }

    [Fact]
    public void Join_a_group_to_its_parent_through_matching_outer_exits()
    {
        int failed = 0, placed = 0;
        for (int seed = 0; seed < 200; seed++)
        {
            if (TryGenerate(seed) is not { } layout) { failed++; continue; }
            List<PlacedChunk> members = layout.Chunks.Where(c => c.Group == "clearing").ToList();
            if (members.Count == 0) continue;
            placed++;

            int entered = members.Min(m => m.Depth);
            PlacedChunk anchor = members.First(m => m.Depth == entered);
            Assert.Contains(layout.Chunks, other => other.Group is null && other.Depth == entered - 1 && FacingExits(other, anchor));
        }

        Assert.InRange(failed, 0, 4);
        Assert.InRange(placed, 150, 200);
    }

    [Fact]
    public void End_the_main_path_with_the_boss_group_when_it_is_the_only_boss()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            ChunkLayout layout = Generator().Generate(Config(5, 7, boss: true), Pool(), seed, [Arena()]);

            Assert.NotNull(layout.BossChunk);
            Assert.Equal("arena", layout.BossChunk!.Group);
            Assert.Equal(4, layout.Chunks.Count(c => c.Group == "arena"));
            Assert.InRange(layout.MainPathLength, 5, 7);
        }
    }

    /// <summary>
    /// Owner decision: a set piece other than the boss's is offered only from the map's MinSetPieceStep on. Every step
    /// before the first set piece is one chunk, so the step a set piece was placed at is its nearest member's depth.
    /// </summary>
    [Fact]
    public void Place_a_set_piece_no_earlier_than_the_maps_first_set_piece_step()
    {
        int failed = 0, placed = 0;
        for (int seed = 0; seed < 200; seed++)
        {
            ProceduralMapConfig config = Config(8, 12, boss: false);
            config.MinSetPieceStep = 5;
            ChunkLayout layout;
            try { layout = Generator().Generate(config, Pool(), seed, [Clearing()]); }
            catch (ProceduralGenerationFailedException) { failed++; continue; }

            List<PlacedChunk> members = layout.Chunks.Where(c => c.Group == "clearing").ToList();
            if (members.Count == 0) continue;
            placed++;
            Assert.True(members.Min(m => m.Depth) >= 5, $"seed {seed}: the clearing starts at step {members.Min(m => m.Depth)}");
        }

        Assert.InRange(failed, 0, 4);
        Assert.InRange(placed, 100, 200);   // measured 137 of 200
    }

    [Fact]
    public void End_the_main_path_with_the_boss_group_whatever_the_first_set_piece_step()
    {
        ProceduralMapConfig config = Config(5, 7, boss: true);
        config.MinSetPieceStep = 20;

        for (int seed = 0; seed < 50; seed++)
            Assert.Equal("arena", Generator().Generate(config, Pool(), seed, [Arena()]).BossChunk?.Group);
    }

    /// <summary>
    /// A retried attempt is routine (many forest builds need one), so it logs at Debug; only running out of
    /// attempts, which leaves the map unenterable for that player, is a Warning.
    /// </summary>
    [Fact]
    public void Log_a_retried_attempt_at_debug_and_only_the_last_failure_as_a_warning()
    {
        int retried = 0;
        for (int seed = 0; seed < 200; seed++)
        {
            var generator = new ProceduralLayoutGenerator(FactoryFor(out CreatureAbilitiesShould.ListLogger logs));
            try { generator.Generate(Config(8, 12, boss: false), Pool(), seed, [Clearing()]); }
            catch (ProceduralGenerationFailedException) { continue; }

            Assert.DoesNotContain(logs.Entries, e => e.Level >= LogLevel.Warning);
            if (logs.Entries.Any(e => e.Level == LogLevel.Debug)) retried++;
        }
        Assert.True(retried > 0, "no seed needed a second attempt, so nothing was checked");

        var corridorOnly = new ProceduralLayoutGenerator(FactoryFor(out CreatureAbilitiesShould.ListLogger failing));
        Assert.Throws<ProceduralGenerationFailedException>(() =>
            corridorOnly.Generate(Config(3, 3, boss: false), [new(Chunk(1, N, "entry", PortalRole.Back), 1f)], seed: 1));
        (LogLevel level, string message) = Assert.Single(failing.Entries, e => e.Level >= LogLevel.Warning);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains("10", message, StringComparison.Ordinal);
        Assert.Equal(9, failing.Entries.Count(e => e.Level == LogLevel.Debug));
    }

    private static ILoggerFactory FactoryFor(out CreatureAbilitiesShould.ListLogger logs)
    {
        logs = new CreatureAbilitiesShould.ListLogger();
        var factory = Substitute.For<ILoggerFactory>();
        factory.CreateLogger(Arg.Any<string>()).Returns(logs);
        return factory;
    }

    [Fact]
    public void Count_depth_in_grid_steps_from_the_entry()
    {
        var corridor = new List<ChunkPoolMember> { new(Chunk(1, N, "entry", PortalRole.Back), 1f), new(Chunk(2, (ushort)(N | S)), 1f) };
        ProceduralMapConfig config = Config(5, 5, boss: false);
        config.BranchChance = 0f;

        ChunkLayout layout = Generator().Generate(config, corridor, seed: 3);

        // A straight corridor north from the entry: depth is the distance along it.
        Assert.All(layout.Chunks, c => Assert.Equal(Math.Abs(c.GridX) + Math.Abs(c.GridZ), c.Depth));
        Assert.Equal(0, layout.EntryChunk.Depth);
        Assert.Equal(5, layout.MainPathLength);
    }

    [Fact]
    public void Generate_what_it_always_did_for_a_pool_without_groups()
    {
        ChunkLayout before = Generator().Generate(Config(6, 9, boss: false), Pool(), seed: 77);
        ChunkLayout after = Generator().Generate(Config(6, 9, boss: false), Pool(), seed: 77, groups: []);

        Assert.Equal(before.Chunks.Select(c => (c.TemplateId.Value, c.GridX, c.GridZ, c.Rotation)),
            after.Chunks.Select(c => (c.TemplateId.Value, c.GridX, c.GridZ, c.Rotation)));
    }

    /// <summary>A pool shaped like the forest's: corridors, corners, a crossroads, a T, a dead end and a boss chunk.</summary>
    private static List<ChunkPoolMember> ForestLikePool() =>
    [
        new(Chunk(1, N, "entry", PortalRole.Back), 1f), new(Chunk(2, (ushort)(N | S), "pack"), 3f), new(Chunk(3, (ushort)(E | W), "pack"), 2f),
        new(Chunk(4, (ushort)(N | E), "pack"), 2f), new(Chunk(5, (ushort)(S | E), "rare"), 1f), new(Chunk(6, (ushort)(N | E | S | W), "pack"), 1f),
        new(Chunk(7, (ushort)(N | E | S)), 1f), new(Chunk(8, S, "pack"), 1f), new(Chunk(9, S, "boss"), 1f),
    ];

    /// <summary>
    /// Layouts the generator produced before set pieces existed (commit d56c104c), for a pool without groups. Any change
    /// to the order or number of random draws changes them, so an old seed would no longer give the same map.
    /// </summary>
    public static TheoryData<int, (string, int, int, int)[]> LayoutsBeforeSetPieces() => new()
    {
        { 7, [("c1", 0, 0, 0), ("c2", 0, 1, 0), ("c7", 0, 2, 1), ("c7", 1, 2, 1), ("c6", 1, 1, 0), ("c7", 1, 0, 3), ("c4", 2, 0, 2),
              ("c7", 2, -1, 0), ("c6", 3, -1, 1), ("c6", 4, -1, 2), ("c7", 4, -2, 2), ("c9", 4, -3, 2), ("c7", -1, 2, 1),
              ("c4", 2, -2, 0), ("c3", 3, 0, 1), ("c7", 3, -2, 3)] },
        { 42, [("c1", 0, 0, 0), ("c6", 0, 1, 2), ("c3", 1, 1, 2), ("c5", 2, 1, 2), ("c3", 2, 2, 1), ("c2", 2, 3, 0), ("c5", 2, 4, 1),
               ("c7", 1, 4, 0), ("c4", 1, 5, 2), ("c6", 0, 5, 1), ("c6", 0, 6, 1), ("c2", 1, 6, 1), ("c2", 2, 6, 1), ("c9", 3, 6, 1),
               ("c1", -1, 1, 1), ("c2", -1, 5, 1), ("c2", -2, 5, 3), ("c7", -3, 5, 0)] },
    };

    [Theory]
    [MemberData(nameof(LayoutsBeforeSetPieces))]
    public void Generate_the_layout_a_seed_gave_before_set_pieces(int seed, (string, int, int, int)[] expected)
    {
        ProceduralMapConfig config = Config(10, 16, boss: true);
        config.BranchMaxDepth = 3;
        Dictionary<ChunkTemplateId, string> names = ForestLikePool().ToDictionary(m => m.Template.Id, m => m.Template.Name);

        ChunkLayout layout = Generator().Generate(config, ForestLikePool(), seed);

        Assert.Equal(seed, layout.Seed);
        Assert.Equal(expected, layout.Chunks.Select(c => (names[c.TemplateId], (int)c.GridX, (int)c.GridZ, (int)c.Rotation)));
        Assert.Equal("c9", names[layout.BossChunk!.TemplateId]);
    }

    private static bool FacingExits(PlacedChunk from, PlacedChunk to)
    {
        int dx = to.GridX - from.GridX, dz = to.GridZ - from.GridZ;
        if (Math.Abs(dx) + Math.Abs(dz) != 1) return false;
        ExitSide side = (dx, dz) switch { (0, 1) => ExitSide.N, (1, 0) => ExitSide.E, (0, -1) => ExitSide.S, _ => ExitSide.W };
        ushort fromExits = ExitMask.Rotate(TemplateOf(from).Exits, from.Rotation);
        ushort toExits = ExitMask.Rotate(TemplateOf(to).Exits, to.Rotation);
        return Enumerable.Range(0, 3).Any(slot =>
            ExitMask.Has(fromExits, side, (ExitSlot)slot) && ExitMask.Has(toExits, ExitMask.Opposite(side), (ExitSlot)slot));
    }

    private static ChunkTemplate TemplateOf(PlacedChunk chunk) =>
        Pool().Select(m => m.Template).Concat(Clearing().Cells.Select(c => c.Template)).First(t => t.Id == chunk.TemplateId);
}
