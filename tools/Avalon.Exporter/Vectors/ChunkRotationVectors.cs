using System.Globalization;
using System.Text;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;

namespace Avalon.Exporter;

/// <summary>
/// Exports the world positions <see cref="ChunkRotation.LocalToWorld"/> produces. A client is held
/// to the server's own numbers because one that gets the pivot wrong agrees with itself perfectly;
/// only the other implementation's output disagrees.
///
/// The layout comes from the production <see cref="ProceduralLayoutGenerator"/> against the
/// forest pool at a fixed seed, so the origins and rotations below are ones the generator
/// really emits. Rotations that layout does not reach are appended by calling LocalToWorld
/// directly -- the point is the function's output, not the layout's shape.
/// </summary>
public static class ChunkRotationVectors
{
    public const string FileName = "rotation-v1.txt";

    private const float CellSize = 30f;
    private const int Seed = 399888156;   // the seed the server's own overlap regression pins

    public static void Write(string outputPath)
    {
        var pool = BuildForestPool();
        var layout = new ProceduralLayoutGenerator().Generate(BuildForestConfig(), pool, Seed);
        var templatesById = pool.ToDictionary(m => m.Template.Id, m => m.Template);

        var rows = new List<string>();
        var rotationsSeen = new HashSet<byte>();

        foreach (var chunk in layout.Chunks)
        {
            var template = templatesById[chunk.TemplateId];
            rotationsSeen.Add(chunk.Rotation);

            Emit(rows, $"corner {template.Name}", chunk, 0f, 0f, 0f);
            Emit(rows, $"corner {template.Name}", chunk, CellSize, 0f, 0f);
            Emit(rows, $"corner {template.Name}", chunk, 0f, 0f, CellSize);
            Emit(rows, $"corner {template.Name}", chunk, CellSize, 0f, CellSize);

            foreach (var slot in template.SpawnSlots.Where(s => s.Tag.Equals("entry", StringComparison.OrdinalIgnoreCase)))
                Emit(rows, $"entryspawn {template.Name}", chunk, slot.LocalX, slot.LocalY, slot.LocalZ);

            foreach (var slot in template.PortalSlots)
                Emit(rows, $"portal-{slot.Role} {template.Name}", chunk, slot.LocalX, slot.LocalY, slot.LocalZ);
        }

        // Every rotation the layout did not reach, called directly. Non-zero origin.y as well as
        // non-zero local.y, so "origin.y + local.y" is held rather than just one of its terms.
        var synthesised = new List<byte>();
        foreach (var rotation in new byte[] { 0, 1, 2, 3 })
        {
            if (rotationsSeen.Contains(rotation)) continue;
            synthesised.Add(rotation);
            var origin = new Vector3(7 * CellSize, 2.5f, 11 * CellSize);
            EmitDirect(rows, "synthetic", origin, rotation, 4f, 1.75f, 26f);
            EmitDirect(rows, "synthetic", origin, rotation, 0f, 0f, 0f);
            EmitDirect(rows, "synthetic", origin, rotation, CellSize, 0f, CellSize);
        }

        var text = new StringBuilder();
        text.Append(Header(layout, rotationsSeen, synthesised));
        foreach (var row in rows)
            text.Append(row).Append('\n');

        Lf.Write(outputPath, text.ToString());

        Console.WriteLine($"wrote {outputPath} ({rows.Count} vectors; layout rotations " +
                          $"{string.Join(",", rotationsSeen.Order())}; synthesised {string.Join(",", synthesised)})");
    }

    private static void Emit(List<string> sink, string label, PlacedChunk chunk, float lx, float ly, float lz)
        => EmitDirect(sink, label, chunk.WorldPos, chunk.Rotation, lx, ly, lz);

    private static void EmitDirect(List<string> sink, string label, Vector3 origin, byte rotation, float lx, float ly, float lz)
    {
        var w = ChunkRotation.LocalToWorld(lx, ly, lz, rotation, CellSize, origin);
        sink.Add(string.Join(' ',
            "v",
            F(origin.x), F(origin.y), F(origin.z),
            rotation.ToString(CultureInfo.InvariantCulture),
            F(CellSize),
            F(lx), F(ly), F(lz),
            "->",
            F(w.x), F(w.y), F(w.z),
            "#", label));
    }

    private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Header(ChunkLayout layout, IReadOnlyCollection<byte> rotationsSeen, IReadOnlyCollection<byte> synthesised) => $"""
        # Chunk rotation - known-answer vectors for ChunkRotation.LocalToWorld.
        #
        # GENERATED FILE. Every world position below is what the server's own ChunkRotation produces
        # for the origin, rotation and local point on the same line, so a client is conformant when it
        # reproduces them, and a change in how the server places chunks arrives here as a diff.
        #
        #   Emitted by  tools/Avalon.Exporter  (Avalon.Server)
        #   Regenerate  dotnet run --project tools/Avalon.Exporter -- rotation
        #   Source      src/Server/Avalon.World.Generation/ChunkRotation.cs
        #
        # THE PIVOT IS THE CHUNK CENTRE, NOT THE CHUNK ORIGIN. Authoring puts the chunk root at the SW
        # corner, so rotating about that corner shifts the footprint into the neighbouring cell and
        # chunks overlap. A client that gets this wrong draws a map that looks like a map, and every
        # self-consistent test it has still passes. Only these numbers disagree.
        #
        #   c      = cellSize / 2
        #   lx,lz  = local.x - c, local.z - c
        #   r=0    rx,rz =  lx,  lz        r=1    rx,rz =  lz, -lx
        #   r=2    rx,rz = -lx, -lz        r=3    rx,rz = -lz,  lx
        #   world  = (origin.x + c + rx,  origin.y + local.y,  origin.z + c + rz)
        #
        # Rows are the four corners, the entry spawn and every portal of each chunk of a layout the
        # production ProceduralLayoutGenerator emitted for the forest pool at seed {layout.Seed}:
        # {layout.Chunks.Count} chunks, at rotations {string.Join(", ", rotationsSeen.Order())}. Rotations that layout does not reach
        # ({string.Join(", ", synthesised)}) are appended as "synthetic" rows calling LocalToWorld directly, with a
        # non-zero origin.y and a non-zero local.y so the vertical term is held too.
        #
        # Format: v originX originY originZ rotation cellSize localX localY localZ -> worldX worldY worldZ # label
        # Floats are round-trip ("R") formatted; the client compares by value, not by byte.


        """;

    /// <summary>
    /// Mirrors the production forest pool the SeedForestProcedural + ExpandForestPool migrations
    /// register, the way the server's own ChunkRotationShould test does. Only exit topology and
    /// slot positions matter here -- geometry is not consulted.
    /// </summary>
    private static List<ChunkPoolMember> BuildForestPool()
    {
        const ushort N_C = 1 << 1;
        const ushort E_C = 1 << 4;
        const ushort S_C = 1 << 7;
        const ushort W_C = 1 << 10;

        return
        [
            new(Chunk(1, "forest_entry_01", N_C, portal: PortalRole.Back, addEntrySpawn: true), 1f),
            new(Chunk(2, "forest_path_01", N_C | S_C, spawnTags: ["pack", "pack"]), 1f),
            new(Chunk(3, "forest_path_02", E_C | S_C, spawnTags: ["pack", "rare"]), 1f),
            new(Chunk(4, "forest_boss_01", S_C, spawnTags: ["boss"], portal: PortalRole.Forward), 1f),
            new(Chunk(5, "forest_path_03", N_C | E_C, spawnTags: ["pack", "pack"]), 1f),
            new(Chunk(6, "forest_path_04", E_C | W_C, spawnTags: ["pack", "pack"]), 1f),
            new(Chunk(7, "forest_junction_t", N_C | S_C | E_C, spawnTags: ["pack", "pack", "rare"]), 1f),
            new(Chunk(8, "forest_junction_x", N_C | S_C | E_C | W_C, spawnTags: ["pack", "rare", "rare"]), 1f),
            new(Chunk(9, "forest_clearing_01", N_C | S_C, spawnTags: ["pack", "pack", "pack", "pack", "rare"]), 1f),
            new(Chunk(10, "forest_deadend_01", S_C, spawnTags: ["pack", "rare"]), 1f),
        ];
    }

    private static ProceduralMapConfig BuildForestConfig() => new()
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
        foreach (var tag in spawnTags ?? [])
            t.SpawnSlots.Add(new ChunkSpawnSlot { Tag = tag, LocalX = 15, LocalY = 1, LocalZ = 15 });
        if (portal is not null)
            t.PortalSlots.Add(new ChunkPortalSlot { Role = portal.Value, LocalX = 15, LocalY = 1, LocalZ = portal.Value == PortalRole.Back ? 5 : 25 });
        return t;
    }
}
