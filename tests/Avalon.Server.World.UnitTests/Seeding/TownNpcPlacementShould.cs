using System.Globalization;
using System.Text;
using System.Text.Json;
using Avalon.Common.Mathematics;
using Avalon.Database.World;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Handlers;
using Avalon.World.ChunkLayouts;
using Avalon.World.Maps.Navigation;
using DotRecast.Detour;
using DotRecast.Recast.Geom;
using DotRecast.Recast.Toolset.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Avalon.Server.World.UnitTests.Seeding;

/// <summary>
/// The town (map 1) in districts: a 2x2 grid of 30 m squares, the arrival square south-west, the forest portal
/// north-west, the market south-east and the bank and inn north-east, with inner walls at X = 30 and Z = 30 opened at
/// 12-18 and 42-48. The seeded NPCs are checked against the real layout and wall geometry, read from the Maps folder
/// copied into the test output, so a geometry or seed change that puts an NPC in a wall or a doorway fails here.
/// </summary>
public class TownNpcPlacementShould
{
    private const float MinWallClearance = 2f;

    /// <summary>How far a doorway lane reaches on each side of its wall (owner decision, 2026-10-01).</summary>
    private const float DoorwayLaneDepth = 3f;

    private const float InnerWall = 30f;
    private static readonly (float From, float To)[] Doorways = [(12f, 18f), (42f, 48f)];

    private sealed record WallBox(string Name, float MinX, float MaxX, float MinZ, float MaxZ)
    {
        public float DistanceTo(float x, float z)
        {
            float dx = Math.Max(Math.Max(MinX - x, 0f), x - MaxX);
            float dz = Math.Max(Math.Max(MinZ - z, 0f), z - MaxZ);
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        public bool Contains(float x, float z) => x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;
    }

    private sealed record TownGeometry(Vector3 Entry, List<WallBox> Walls, List<(string Chunk, Vector3 Origin)> Chunks);

    private static readonly Lazy<TownGeometry> Town = new(ReadTown, isThreadSafe: true);
    private static readonly Lazy<DtNavMesh> TownNavMesh = new(BakeTown, isThreadSafe: true);

    private static string MapsDir => Path.Combine(AppContext.BaseDirectory, "Maps");

    private static TownGeometry ReadTown()
    {
        using JsonDocument layout = JsonDocument.Parse(File.ReadAllText(Path.Combine(MapsDir, "TownLayouts", "1.json")));
        float cellSize = layout.RootElement.GetProperty("cellSize").GetSingle();
        Vector3? entry = null;
        var walls = new List<WallBox>();
        var chunks = new List<(string, Vector3)>();

        foreach (JsonElement placement in layout.RootElement.GetProperty("chunks").EnumerateArray())
        {
            string name = placement.GetProperty("chunkName").GetString()!;
            // Only unrotated placements are read here; a rotated town chunk would need ChunkRotation on every wall.
            Assert.Equal(0, placement.GetProperty("rotation").GetInt32());
            var origin = new Vector3(placement.GetProperty("gridX").GetInt32() * cellSize, 0f,
                placement.GetProperty("gridZ").GetInt32() * cellSize);
            chunks.Add((name, origin));

            if (placement.GetProperty("isEntry").GetBoolean())
            {
                JsonElement spawn = placement.GetProperty("entrySpawn");
                entry = ChunkRotation.LocalToWorld(spawn.GetProperty("localX").GetSingle(), spawn.GetProperty("localY").GetSingle(),
                    spawn.GetProperty("localZ").GetSingle(), 0, cellSize, origin);
            }

            string? current = null;
            float minX = 0, maxX = 0, minZ = 0, maxZ = 0;
            bool any = false;
            void Flush()
            {
                if (current is not null && any && current.StartsWith("Wall", StringComparison.Ordinal))
                    walls.Add(new WallBox($"{name}/{current}", origin.x + minX, origin.x + maxX, origin.z + minZ, origin.z + maxZ));
            }

            foreach (string raw in File.ReadAllLines(Path.Combine(MapsDir, "Chunks", $"{name}.obj")))
            {
                string line = raw.Trim();
                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (line.StartsWith("o ", StringComparison.Ordinal))
                {
                    Flush();
                    current = parts[1];
                    any = false;
                }
                else if (line.StartsWith("v ", StringComparison.Ordinal))
                {
                    float x = float.Parse(parts[1], CultureInfo.InvariantCulture);
                    float z = float.Parse(parts[3], CultureInfo.InvariantCulture);
                    if (!any) { minX = maxX = x; minZ = maxZ = z; any = true; }
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z);
                }
            }

            Flush();
        }

        Assert.NotNull(entry);
        Assert.NotEmpty(walls);
        return new TownGeometry(entry!.Value, walls, chunks);
    }

    /// <summary>The town navmesh baked as ChunkLayoutNavmeshBuilder bakes it, from the objs in the output folder.</summary>
    private static DtNavMesh BakeTown()
    {
        var sb = new StringBuilder();
        int offset = 0;
        foreach ((string name, Vector3 origin) in Town.Value.Chunks)
        {
            int count = 0;
            foreach (string raw in File.ReadAllLines(Path.Combine(MapsDir, "Chunks", $"{name}.obj")))
            {
                string line = raw.Trim();
                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (line.StartsWith("v ", StringComparison.Ordinal))
                {
                    Vector3 w = ChunkRotation.LocalToWorld(float.Parse(parts[1], CultureInfo.InvariantCulture),
                        float.Parse(parts[2], CultureInfo.InvariantCulture), float.Parse(parts[3], CultureInfo.InvariantCulture),
                        0, 30f, origin);
                    sb.Append(CultureInfo.InvariantCulture, $"v {w.x} {w.y} {w.z}\n");
                    count++;
                }
                else if (line.StartsWith("f ", StringComparison.Ordinal))
                {
                    sb.Append('f');
                    for (int i = 1; i < parts.Length; i++)
                        sb.Append(' ').Append(int.Parse(parts[i].Split('/')[0], CultureInfo.InvariantCulture) + offset);
                    sb.Append('\n');
                }
            }

            offset += count;
        }

        string combined = Path.Combine(Path.GetTempPath(), $"avalon-town-npcs-{Guid.NewGuid():N}.obj");
        File.WriteAllText(combined, sb.ToString());
        try
        {
            var result = new TileNavMeshBuilder().Build(RcSampleInputGeomProvider.LoadFile(combined), NavmeshBuildSettings.Create());
            Assert.NotNull(result?.NavMesh);
            return result!.NavMesh;
        }
        finally
        {
            File.Delete(combined);
        }
    }

    private static List<MapCreatureSpawn> SeededSpawns()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        return context.MapCreatureSpawns.AsNoTracking().ToList();
    }

    private static (float X, float Z) WorldOf(MapCreatureSpawn spawn) =>
        (Town.Value.Entry.x + spawn.OffsetX, Town.Value.Entry.z + spawn.OffsetZ);

    private static MapCreatureSpawn SpawnOf(List<MapCreatureSpawn> spawns, ulong template) =>
        spawns.Single(s => s.CreatureTemplateId.Value == template);

    [Fact]
    public void Read_the_entry_spawn_at_fifteen_fifteen()
    {
        Assert.Equal(15f, Town.Value.Entry.x);
        Assert.Equal(15f, Town.Value.Entry.z);
    }

    /// <summary>Template, world X and Z, and the south-west corner of the square it belongs in.</summary>
    [Theory]
    [InlineData(1ul, 15f, 19f, 0f, 0f)]      // Uriel, arrival square
    [InlineData(2ul, 19f, 27f, 0f, 0f)]      // Borin Stoutbeard, arrival square
    [InlineData(12ul, 40f, 4f, 30f, 0f)]     // Garrick Emberforge, market
    [InlineData(13ul, 50f, 4f, 30f, 0f)]     // Hilde Brassbuckle, market
    [InlineData(14ul, 56f, 15f, 30f, 0f)]    // Tobin Marrowfield, market
    [InlineData(11ul, 40f, 56f, 30f, 30f)]   // Marta Ledgerwell, bank and inn
    [InlineData(3ul, 50f, 56f, 30f, 30f)]    // Innkeeper, bank and inn
    public void Stand_each_npc_where_the_spec_puts_it(ulong template, float x, float z, float squareX, float squareZ)
    {
        MapCreatureSpawn spawn = SpawnOf(SeededSpawns(), template);
        (float worldX, float worldZ) = WorldOf(spawn);

        Assert.Equal(x, worldX, 3);
        Assert.Equal(z, worldZ, 3);
        Assert.Equal(0f, spawn.OffsetY);
        Assert.InRange(worldX, squareX, squareX + 30f);
        Assert.InRange(worldZ, squareZ, squareZ + 30f);
    }

    [Fact]
    public void Keep_every_npc_two_metres_from_every_wall()
    {
        foreach (MapCreatureSpawn spawn in SeededSpawns())
        {
            (float x, float z) = WorldOf(spawn);
            foreach (WallBox wall in Town.Value.Walls)
            {
                float clearance = wall.DistanceTo(x, z);
                Assert.True(clearance >= MinWallClearance,
                    $"creature {spawn.CreatureTemplateId.Value} at ({x}, {z}) is {clearance:0.00} m from {wall.Name}");
            }
        }
    }

    /// <summary>The doorway constants below are only meaningful while the geometry really has those openings.</summary>
    [Fact]
    public void Find_every_doorway_open_in_the_town_geometry()
    {
        foreach ((float from, float to) in Doorways)
        {
            float middle = (from + to) / 2f;
            Assert.DoesNotContain(Town.Value.Walls, w => w.Contains(InnerWall, middle));   // a gap in the X = 30 wall
            Assert.DoesNotContain(Town.Value.Walls, w => w.Contains(middle, InnerWall));   // a gap in the Z = 30 wall
            Assert.Contains(Town.Value.Walls, w => w.Contains(InnerWall, from - 1f));      // the wall closes either side
            Assert.Contains(Town.Value.Walls, w => w.Contains(InnerWall, to + 1f));
        }
    }

    [Fact]
    public void Keep_every_npc_off_the_doorway_lanes()
    {
        foreach (MapCreatureSpawn spawn in SeededSpawns())
        {
            (float x, float z) = WorldOf(spawn);
            foreach ((float from, float to) in Doorways)
            {
                bool inXWallLane = Math.Abs(x - InnerWall) <= DoorwayLaneDepth && z >= from && z <= to;
                bool inZWallLane = Math.Abs(z - InnerWall) <= DoorwayLaneDepth && x >= from && x <= to;
                Assert.False(inXWallLane || inZWallLane,
                    $"creature {spawn.CreatureTemplateId.Value} at ({x}, {z}) stands in the {from}-{to} doorway lane");
            }
        }
    }

    /// <summary>Template, and the point it faces: the arrival point, the market centre or the bank and inn centre.</summary>
    [Theory]
    [InlineData(1ul, 15f, 15f)]
    [InlineData(2ul, 15f, 15f)]
    [InlineData(12ul, 45f, 15f)]
    [InlineData(13ul, 45f, 15f)]
    [InlineData(14ul, 45f, 15f)]
    [InlineData(11ul, 45f, 45f)]
    [InlineData(3ul, 45f, 45f)]
    public void Face_what_the_spec_says(ulong template, float faceX, float faceZ)
    {
        MapCreatureSpawn spawn = SpawnOf(SeededSpawns(), template);
        (float x, float z) = WorldOf(spawn);

        double expected = (Math.Atan2(faceX - x, faceZ - z) * 180.0 / Math.PI + 360.0) % 360.0;
        double delta = Math.Abs(((spawn.Facing - expected) % 360.0 + 540.0) % 360.0 - 180.0);

        Assert.True(delta <= 0.5, $"creature {template} faces {spawn.Facing}, expected {expected:0.###}");
    }

    [Fact]
    public void Stand_every_npc_on_the_town_navmesh_within_reach_of_the_entry()
    {
        var navigator = new MapNavigator(NullLoggerFactory.Instance);
        navigator.LoadFromNavMesh(TownNavMesh.Value);
        Vector3 entry = Town.Value.Entry + new Vector3(0f, 1f, 0f);

        foreach (MapCreatureSpawn spawn in SeededSpawns())
        {
            (float x, float z) = WorldOf(spawn);
            var at = new Vector3(x, 1f, z);

            Assert.Equal(NavmeshGroundKind.Under, navigator.FindGround(at, out _));

            List<Vector3> path = navigator.FindPath(entry, at);
            Assert.NotEmpty(path);
            Vector3 end = path[^1];
            float gap = MathF.Sqrt((end.x - x) * (end.x - x) + (end.z - z) * (end.z - z));
            Assert.True(gap <= 1f, $"the path from the entry to creature {spawn.CreatureTemplateId.Value} ends {gap:0.00} m short");
        }
    }
}
