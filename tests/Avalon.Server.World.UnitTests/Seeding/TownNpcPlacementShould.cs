using System.Globalization;
using System.Text;
using System.Text.Json;
using Avalon.ChunkGen;
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

namespace Avalon.Server.World.UnitTests.Seeding;

/// <summary>
/// The town (map 1) in districts: a 2x2 grid of 30 m squares, the arrival square south-west, the forest portal
/// north-west, the market south-east and the bank and inn north-east, with inner walls at X = 30 and Z = 30 opened at
/// 12-18 and 42-48. The seeded NPCs are checked against the real layout and wall geometry, read from the Maps folder
/// copied into the test output, so a geometry or seed change that puts an NPC in a wall or a doorway fails here.
/// Each NPC stands in front of its own building, on the side the client's fixed camera sees and clear of its roof: on
/// a porch step, in front of a counter or the smithy's lean-to, on the bank's step, at least 1 m from any solid and
/// within 3 m of its building (TownPieces).
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

    private sealed record TownGeometry(Vector3 Entry, float CellSize, List<WallBox> Walls, List<(string Chunk, Vector3 Origin)> Chunks);

    private static readonly Lazy<TownGeometry> Town = new(ReadTown, isThreadSafe: true);
    private static readonly Lazy<DtNavMesh> TownNavMesh = new(BakeTown, isThreadSafe: true);
    private static readonly Lazy<List<MapCreatureSpawn>> TownSpawns = new(ReadTownSpawns, isThreadSafe: true);

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
        return new TownGeometry(entry!.Value, cellSize, walls, chunks);
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
                        0, Town.Value.CellSize, origin);
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

    /// <summary>The seeded spawns on the town (map 1), read once for every test in the class.</summary>
    private static List<MapCreatureSpawn> SeededSpawns() => TownSpawns.Value;

    private static List<MapCreatureSpawn> ReadTownSpawns()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        List<MapCreatureSpawn> spawns = context.MapCreatureSpawns.AsNoTracking().ToList()
            .Where(s => s.MapTemplateId.Value == 1).ToList();
        Assert.NotEmpty(spawns);
        return spawns;
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
    [InlineData(1ul, 16.6f, 23f, 0f, 0f)]      // Uriel, in front of the town hall's porch
    [InlineData(2ul, 16.9f, 37f, 0f, 30f)]     // Borin Stoutbeard, in front of the hunter's lodge porch
    [InlineData(12ul, 39f, 18.2f, 30f, 0f)]    // Garrick Emberforge, in front of the smithy's lean-to
    [InlineData(13ul, 53.9f, 15f, 30f, 0f)]    // Hilde Brassbuckle, in front of the armourer's counter
    [InlineData(14ul, 51.5f, 22.2f, 30f, 0f)]  // Tobin Marrowfield, in front of the general-goods counter
    [InlineData(11ul, 36.8f, 49.4f, 30f, 30f)] // Marta Ledgerwell, the bank's lower step
    [InlineData(3ul, 50f, 47.2f, 30f, 30f)]    // Innkeeper, in front of the inn's porch
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

    /// <summary>
    /// The doorway constants are only meaningful while the geometry really has those openings: in both inner walls
    /// (X = 30 and Z = 30), each opening is clear of every wall across its whole width and closed on either side.
    /// </summary>
    [Fact]
    public void Find_every_doorway_open_in_the_town_geometry()
    {
        const float Step = 0.25f;
        foreach ((float from, float to) in Doorways)
        {
            // Strictly inside the opening: the walls either side end exactly at its edges.
            int samples = (int)MathF.Round((to - from) / Step) - 1;
            for (int i = 1; i <= samples; i++)
            {
                float along = from + i * Step;
                Assert.DoesNotContain(Town.Value.Walls, w => w.Contains(InnerWall, along));   // a gap in the X = 30 wall
                Assert.DoesNotContain(Town.Value.Walls, w => w.Contains(along, InnerWall));   // a gap in the Z = 30 wall
            }

            Assert.Contains(Town.Value.Walls, w => w.Contains(InnerWall, from - 1f));         // the X = 30 wall closes either side
            Assert.Contains(Town.Value.Walls, w => w.Contains(InnerWall, to + 1f));
            Assert.Contains(Town.Value.Walls, w => w.Contains(from - 1f, InnerWall));         // the Z = 30 wall closes either side
            Assert.Contains(Town.Value.Walls, w => w.Contains(to + 1f, InnerWall));
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
    [InlineData(2ul, 15f, 37f)]
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

    /// <summary>Which building each NPC belongs to (TownPieces.Building names).</summary>
    private static readonly Dictionary<ulong, string> BuildingOf = new()
    {
        [1] = "Town hall",
        [2] = "Hunter's lodge",
        [12] = "Smithy",
        [13] = "Armourer's stall",
        [14] = "General-goods stall",
        [3] = "Inn",
        [11] = "Bank",
    };

    /// <summary>Every solid of the town (a non-walkable piece standing below 2 m), with its distance to a world point.</summary>
    private static IEnumerable<(TownSquare Square, TownPiece Piece, float Distance)> Solids(float x, float z) =>
        TownPieces.Squares().SelectMany(s => s.Pieces
            .Where(p => !p.Walkable && p.Y0 < TownRules.SolidBelow)
            .Select(p => (s, p, p.DistanceTo(x - s.Origin.X, z - s.Origin.Z))));

    [Fact]
    public void Stand_each_npc_outside_every_building_and_within_three_metres_of_its_own()
    {
        foreach (MapCreatureSpawn spawn in SeededSpawns())
        {
            (float x, float z) = WorldOf(spawn);
            string building = BuildingOf[spawn.CreatureTemplateId.Value];

            foreach ((TownSquare square, TownPiece piece, float distance) in Solids(x, z))
                Assert.True(distance >= TownRules.NpcClearance - 1e-4f,
                    $"creature {spawn.CreatureTemplateId.Value} at ({x}, {z}) is {distance:0.00} m from {square.Name} {piece.Building}/{piece.Part}");

            float own = Solids(x, z).Where(s => s.Piece.Building == building).Min(s => s.Distance);
            Assert.True(own <= TownRules.NpcBuildingReach, $"creature {spawn.CreatureTemplateId.Value} is {own:0.00} m from {building}");
        }
    }

    /// <summary>The client's fixed camera (owner decision, 2026-10-02): yaw 45, 60 degrees below horizontal.</summary>
    private const float CameraYawDeg = 45f;
    private const float CameraPitchDeg = 60f;

    /// <summary>
    /// The client's perspective rig: it looks at a pivot this far above the player, from a zoom distance between these
    /// (IsometricCameraComponent). A player within <see cref="FramedFraction" /> of that distance from an NPC keeps it
    /// well inside the 45-degree frame.
    /// </summary>
    private const float CameraPivotHeight = 1.75f;
    private static readonly float[] CameraDistances = [5f, 10f, 15f, 20f, 25f];
    private const float FramedFraction = 0.35f;
    private const float PlayerGridStep = 1f;

    /// <summary>A standing NPC's head, and how finely the line from it to the camera is walked.</summary>
    private const float HeadHeight = 1.8f;
    private const float SightStep = 0.05f;

    /// <summary>Whether a world point lies inside a solid piece or a wall of the town.</summary>
    private static string? SolidAt(float x, float y, float z)
    {
        foreach (TownSquare square in TownPieces.Squares())
        {
            float lx = x - square.Origin.X, lz = z - square.Origin.Z;
            if (lx < 0f || lx > TownSquare.CellSize || lz < 0f || lz > TownSquare.CellSize)
                continue;
            foreach (WallSegment wall in square.Walls)
                if (y <= WallSegment.Height && wall.DistanceTo(lx, lz) <= 0f)
                    return $"{square.Name} {wall.Name}";
            foreach (TownPiece piece in square.Pieces)
            {
                if (piece.DistanceTo(lx, lz) > 0f || y < piece.Y0)
                    continue;
                float top = piece is GablePiece gable ? GableHeight(gable, lx, lz) : piece.Top;
                if (y <= top)
                    return $"{square.Name} {piece.Building}/{piece.Part}";
            }
        }
        return null;
    }

    /// <summary>A gable's roof height over a point of its footprint: the eaves at the sides, the ridge on its axis.</summary>
    private static float GableHeight(GablePiece g, float x, float z)
    {
        float across = g.RidgeAlongX ? MathF.Abs(z - (g.MinZ + g.MaxZ) / 2f) / ((g.MaxZ - g.MinZ) / 2f)
                                     : MathF.Abs(x - (g.MinX + g.MaxX) / 2f) / ((g.MaxX - g.MinX) / 2f);
        return g.Ridge - (g.Ridge - g.Eaves) * Math.Clamp(across, 0f, 1f);
    }

    /// <summary>
    /// The camera sees every NPC: from every zoom, and with the player anywhere that keeps the NPC well in frame, the
    /// line from its head to the camera's eye crosses no piece and no wall. A roof over the NPC, a building between it
    /// and the camera, or a stall it stands behind each fails here by name and by where the player stood.
    /// </summary>
    [Fact]
    public void Stand_every_npc_where_the_camera_sees_it()
    {
        float yaw = CameraYawDeg * MathF.PI / 180f, pitch = CameraPitchDeg * MathF.PI / 180f;
        var toCamera = new Vector3(-MathF.Cos(pitch) * MathF.Sin(yaw), MathF.Sin(pitch), -MathF.Cos(pitch) * MathF.Cos(yaw));

        foreach (MapCreatureSpawn spawn in SeededSpawns())
        {
            (float x, float z) = WorldOf(spawn);
            var head = new Vector3(x, HeadHeight, z);
            foreach (float distance in CameraDistances)
            {
                float reach = FramedFraction * distance;
                for (float dx = -reach; dx <= reach; dx += PlayerGridStep)
                    for (float dz = -reach; dz <= reach; dz += PlayerGridStep)
                    {
                        if (dx * dx + dz * dz > reach * reach)
                            continue;
                        Vector3 eye = new Vector3(x + dx, CameraPivotHeight, z + dz) + toCamera * distance;
                        Vector3 line = eye - head;
                        float length = MathF.Sqrt(line.x * line.x + line.y * line.y + line.z * line.z);
                        for (float t = SightStep; t < length; t += SightStep)
                        {
                            float f = t / length;
                            string? hit = SolidAt(head.x + line.x * f, head.y + line.y * f, head.z + line.z * f);
                            Assert.True(hit is null,
                                $"creature {spawn.CreatureTemplateId.Value} at ({x}, {z}) is hidden by {hit} from a player at ({x + dx}, {z + dz}), zoom {distance}");
                        }
                    }
            }
        }
    }

    /// <summary>The player's own walks: each doorway crossed both ways, the gate arch passed, and the portal reached from the entry.</summary>
    [Fact]
    public void Walk_through_every_doorway_and_the_gate_arch()
    {
        var navigator = new MapNavigator(NullLoggerFactory.Instance);
        navigator.LoadFromNavMesh(TownNavMesh.Value);

        (Vector3 From, Vector3 To, string Label)[] walks =
        [
            (new Vector3(15f, 0.15f, 25f), new Vector3(15f, 0.15f, 35f), "the Z = 30 doorway at X = 15, north"),
            (new Vector3(45f, 0.15f, 25f), new Vector3(45f, 0.15f, 35f), "the Z = 30 doorway at X = 45, north"),
            (new Vector3(25f, 0.15f, 15f), new Vector3(35f, 0.15f, 15f), "the X = 30 doorway at Z = 15, east"),
            (new Vector3(25f, 0.15f, 45f), new Vector3(35f, 0.15f, 45f), "the X = 30 doorway at Z = 45, east"),
            (new Vector3(15f, 0.15f, 38f), new Vector3(15f, 0.15f, 45f), "under the gate arch to the portal"),
        ];
        foreach ((Vector3 from, Vector3 to, string label) in walks)
        {
            Vector3 stop = navigator.RaycastWalkable(from, to);
            Assert.True(MathF.Abs(stop.x - to.x) < 0.5f && MathF.Abs(stop.z - to.z) < 0.5f, $"{label}: stopped at ({stop.x:0.00}, {stop.z:0.00})");
            Vector3 back = navigator.RaycastWalkable(to, from);
            Assert.True(MathF.Abs(back.x - from.x) < 0.5f && MathF.Abs(back.z - from.z) < 0.5f, $"{label}, back: stopped at ({back.x:0.00}, {back.z:0.00})");
        }

        List<Vector3> path = navigator.FindPath(Town.Value.Entry + new Vector3(0f, 1f, 0f), new Vector3(15f, 1f, 45f));
        Assert.NotEmpty(path);
        Assert.True(MathF.Abs(path[^1].x - 15f) <= 1f && MathF.Abs(path[^1].z - 45f) <= 1f, $"the path to the portal ends at {path[^1]}");
    }
}
