using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.ChunkLayouts;

public class ChunkRotationShould
{
    private const float CellSize = 30f;

    // Bit mask: side N(0..2), E(3..5), S(6..8), W(9..11). Slot L,C,R within side.
    // N-Center = bit 1 (2)   E-Center = bit 4 (16)
    // S-Center = bit 7 (128) W-Center = bit 10 (1024)
    private const ushort N_C = 1 << 1;
    private const ushort E_C = 1 << 4;
    private const ushort S_C = 1 << 7;
    private const ushort W_C = 1 << 10;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Footprint_stays_within_declared_cell_for_any_rotation(byte rotation)
    {
        // Chunk authored with origin at SW corner, footprint (0..30, *, 0..30).
        var corners = new (float x, float z)[]
        {
            (0f, 0f), (CellSize, 0f), (CellSize, CellSize), (0f, CellSize),
        };
        // Place at an arbitrary cell (3, 5) so a SW-corner-pivot bug would push the
        // footprint into a neighbour cell.
        var origin = new Vector3(3 * CellSize, 0f, 5 * CellSize);
        var minX = origin.x;
        var maxX = origin.x + CellSize;
        var minZ = origin.z;
        var maxZ = origin.z + CellSize;

        foreach (var c in corners)
        {
            var w = ChunkRotation.LocalToWorld(c.x, 0f, c.z, rotation, CellSize, origin);
            Assert.InRange(w.x, minX - 0.001f, maxX + 0.001f);
            Assert.InRange(w.z, minZ - 0.001f, maxZ + 0.001f);
        }
    }

    [Fact]
    public void Identity_rotation_passes_through()
    {
        var origin = new Vector3(60f, 0f, 90f);
        var w = ChunkRotation.LocalToWorld(15f, 1f, 5f, rotation: 0, CellSize, origin);
        Assert.Equal(75f, w.x, precision: 3);
        Assert.Equal(1f, w.y, precision: 3);
        Assert.Equal(95f, w.z, precision: 3);
    }

    [Fact]
    public void Center_point_stays_at_cell_center_under_any_rotation()
    {
        var origin = new Vector3(0f, 0f, 0f);
        for (byte r = 0; r < 4; r++)
        {
            var w = ChunkRotation.LocalToWorld(CellSize / 2, 0f, CellSize / 2, r, CellSize, origin);
            Assert.Equal(CellSize / 2, w.x, precision: 3);
            Assert.Equal(CellSize / 2, w.z, precision: 3);
        }
    }

    /// <summary>
    /// Sanity check that 50 distinct seeds against the production forest pool all produce
    /// a valid layout — no <see cref="ProceduralGenerationFailedException"/>. Regression
    /// for the trap-then-bail-out case where the generator picked a single-exit chunk
    /// (boss / deadend) mid-path and could not walk forward, killing the user's
    /// portal entry with "Failed to generate layout for map 2 after 3 attempts".
    /// </summary>
    [Fact]
    public void Forest_pool_generates_for_many_seeds()
    {
        var (pool, cfg) = BuildForestPool();
        var gen = new ProceduralChunkLayoutSource(
            NullLoggerFactory.Instance,
            Substitute.For<IChunkLibrary>(),
            Substitute.For<IServiceScopeFactory>());

        for (int seed = 0; seed < 50; seed++)
        {
            var layout = gen.Generate(cfg, pool, seed);
            Assert.NotNull(layout);
            Assert.NotNull(layout.BossChunk);
        }
    }

    /// <summary>
    /// Regression for the overlap caught at runtime: seed 399888156 with the production
    /// forest pool produced two chunks whose floor footprints occupied the same world cell
    /// because the bake/visualizer rotated around the SW corner instead of the chunk centre.
    /// With <see cref="ChunkRotation.LocalToWorld"/> in place every PlacedChunk's footprint
    /// must lie inside its declared (GridX, GridZ) cell — which is sufficient to guarantee
    /// no two chunks overlap (generator already ensures unique cells).
    /// </summary>
    [Fact]
    public void Forest_pool_seed_399888156_produces_non_overlapping_footprints()
    {
        var (pool, cfg) = BuildForestPool();
        var gen = new ProceduralChunkLayoutSource(
            NullLoggerFactory.Instance,
            Substitute.For<IChunkLibrary>(),
            Substitute.For<IServiceScopeFactory>());

        var layout = gen.Generate(cfg, pool, seed: 399888156);

        // Each chunk's transformed AABB must lie inside its declared (GridX, GridZ) cell.
        foreach (var chunk in layout.Chunks)
        {
            var minX = chunk.GridX * CellSize;
            var maxX = minX + CellSize;
            var minZ = chunk.GridZ * CellSize;
            var maxZ = minZ + CellSize;
            (float x, float z)[] corners = { (0, 0), (CellSize, 0), (CellSize, CellSize), (0, CellSize) };
            foreach (var c in corners)
            {
                var w = ChunkRotation.LocalToWorld(c.x, 0f, c.z, chunk.Rotation, layout.CellSize, chunk.WorldPos);
                Assert.InRange(w.x, minX - 0.001f, maxX + 0.001f);
                Assert.InRange(w.z, minZ - 0.001f, maxZ + 0.001f);
            }
        }
    }

    private static ChunkTemplate Chunk(int id, string name, ushort exits, string[]? spawnTags = null, PortalRole? portal = null, bool addEntrySpawn = false)
    {
        var t = new ChunkTemplate
        {
            Id = new ChunkTemplateId(id),
            Name = name,
            CellSize = CellSize,
            Exits = exits,
        };
        if (addEntrySpawn)
            t.SpawnSlots.Add(new ChunkSpawnSlot { Tag = "entry", LocalX = 15, LocalY = 1, LocalZ = 5 });
        foreach (var tag in spawnTags ?? Array.Empty<string>())
            t.SpawnSlots.Add(new ChunkSpawnSlot { Tag = tag, LocalX = 15, LocalY = 1, LocalZ = 15 });
        if (portal is not null)
            t.PortalSlots.Add(new ChunkPortalSlot { Role = portal.Value, LocalX = 15, LocalY = 1, LocalZ = portal.Value == PortalRole.Back ? 5 : 25 });
        return t;
    }

    /// <summary>
    /// Mirrors the production forest pool (10 chunks) registered by SeedForestProcedural
    /// + ExpandForestPool migrations. Only exit topology + slot tags matter for the
    /// generator — geometry is not consulted.
    /// </summary>
    private static (List<ChunkPoolMember> pool, ProceduralMapConfig cfg) BuildForestPool()
    {
        var pool = new List<ChunkPoolMember>
        {
            new(Chunk(1, "forest_entry_01",     N_C,           portal: PortalRole.Back,    addEntrySpawn: true), 1f),
            new(Chunk(2, "forest_path_01",      N_C | S_C,     spawnTags: new[] { "pack", "pack" }), 1f),
            new(Chunk(3, "forest_path_02",      E_C | S_C,     spawnTags: new[] { "pack", "rare" }), 1f),
            new(Chunk(4, "forest_boss_01",      S_C,           spawnTags: new[] { "boss" }, portal: PortalRole.Forward), 1f),
            new(Chunk(5, "forest_path_03",      N_C | E_C,     spawnTags: new[] { "pack", "pack" }), 1f),
            new(Chunk(6, "forest_path_04",      E_C | W_C,     spawnTags: new[] { "pack", "pack" }), 1f),
            new(Chunk(7, "forest_junction_t",   N_C | S_C | E_C, spawnTags: new[] { "pack", "pack", "rare" }), 1f),
            new(Chunk(8, "forest_junction_x",   N_C | S_C | E_C | W_C, spawnTags: new[] { "pack", "rare", "rare" }), 1f),
            new(Chunk(9, "forest_clearing_01",  N_C | S_C,     spawnTags: new[] { "pack", "pack", "pack", "pack", "rare" }), 1f),
            new(Chunk(10,"forest_deadend_01",   S_C,           spawnTags: new[] { "pack", "rare" }), 1f),
        };
        var cfg = new ProceduralMapConfig
        {
            MapTemplateId = new MapTemplateId(2),
            ChunkPoolId = new ChunkPoolId(1),
            SpawnTableId = new SpawnTableId(1),
            MainPathMin = 4,
            MainPathMax = 7,
            BranchChance = 0.4f,
            BranchMaxDepth = 2,
            HasBoss = true,
            BackPortalTargetMapId = 1,
            ForwardPortalTargetMapId = null,
        };
        return (pool, cfg);
    }
}
