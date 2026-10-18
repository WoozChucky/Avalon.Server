using System.Text.Json;
using Avalon.ChunkGen;
using Avalon.Common.Mathematics;
using Avalon.World.Maps.Navigation;

namespace Avalon.Server.World.UnitTests.Procedural;

/// <summary>
/// The forest's generated pieces (forest content pass): what is committed is what the tool writes, every slot and exit
/// is clear of blockers, every doodad slot stands off the paths, and the baked navmesh lets a creature stand on every
/// slot (doodad slots included) and walk from every exit to every spawn slot, across a set piece's open inner edges too.
/// </summary>
public class ForestPiecesShould
{
    private const float SlotClearance = 2.5f;
    private const float EdgeClearance = 2f;

    // Doodad slots stand off the paths and clear of blockers and spawns (doodads spec): a "doodad_large" slot may hold a
    // solid doodad, so it keeps further away. A path is the segment from an exit's throat centre to the piece centre.
    private const float DoodadSlotClearance = 2.5f;
    private const float LargeDoodadSlotClearance = 4f;
    private const float DoodadPathClearance = 4f;
    private const float LargeDoodadPathClearance = 5f;
    private const float DoodadSpawnClearance = 3f;
    private const float LargeDoodadSpawnClearance = 4f;

    private static string Maps => Path.Combine(RepositoryRoot(), "src", "Server", "Avalon.Server.World", "Maps");

    public static IEnumerable<object[]> Singles() => ForestPieces.Singles().Select(p => new object[] { p.Name });

    public static IEnumerable<object[]> Groups() => ForestPieces.Groups().Select(g => new object[] { g.Name });

    /// <summary>The files the tool's run writes (ChunkFiles.For over the singles and the set pieces, as ChunkGenCli calls it).</summary>
    [Fact]
    public void Match_the_committed_files()
    {
        foreach ((string name, string obj, string json, _) in ChunkFiles.For(ForestPieces.Singles(), ForestPieces.Groups()))
        {
            Assert.Equal(obj, Lf(File.ReadAllText(Path.Combine(Maps, "Chunks", name + ".obj"))));
            Assert.Equal(json, Lf(File.ReadAllText(Path.Combine(Maps, "Chunks", name + ".json"))));
        }
    }

    [Fact]
    public void Generate_eight_pieces_and_three_set_pieces()
    {
        Assert.Equal(8, ForestPieces.Singles().Count);
        Assert.Equal(["forest_arena", "forest_clearing_big", "forest_grove_ruin"], ForestPieces.Groups().Select(g => g.Name).Order());
        Assert.Equal(20, ChunkFiles.For(ForestPieces.Singles(), ForestPieces.Groups()).Count);
    }

    [Fact]
    public void Keep_every_slot_clear_of_blockers_and_edges()
    {
        foreach (ChunkPiece piece in Pieces())
        {
            foreach (Slot slot in piece.Slots)
            {
                Assert.InRange(slot.X, EdgeClearance, ChunkPiece.CellSize - EdgeClearance);
                Assert.InRange(slot.Z, EdgeClearance, ChunkPiece.CellSize - EdgeClearance);
                foreach (Blocker blocker in piece.Blockers)
                {
                    Assert.True(blocker.DistanceTo(slot.X, slot.Z) >= SlotClearance,
                        $"{piece.Name}: {slot} is {blocker.DistanceTo(slot.X, slot.Z):0.00} m from {blocker}");
                }
            }
        }
    }

    [Fact]
    public void Keep_every_doodad_slot_off_paths_and_clear_of_blockers_spawns_and_edges()
    {
        Assert.All(ForestPieces.Singles(), p =>
        {
            Assert.Equal(2, p.DoodadSlots.Count(s => s.Tag == "doodad"));
            Assert.Equal(1, p.DoodadSlots.Count(s => s.Tag == "doodad_large"));
        });
        Assert.All(ForestPieces.Groups().SelectMany(g => g.Members()), m => Assert.Equal(1, m.Piece.DoodadSlots.Count(s => s.Tag == "doodad")));

        foreach (ChunkPiece piece in Pieces())
        {
            foreach (DoodadSlot slot in piece.DoodadSlots)
            {
                Assert.True(slot.Tag is "doodad" or "doodad_large", $"{piece.Name}: {slot} has an unknown tag");
                bool large = slot.Tag == "doodad_large";
                Assert.InRange(slot.X, EdgeClearance, ChunkPiece.CellSize - EdgeClearance);
                Assert.InRange(slot.Z, EdgeClearance, ChunkPiece.CellSize - EdgeClearance);
                foreach (Blocker blocker in piece.Blockers)
                {
                    Assert.True(blocker.DistanceTo(slot.X, slot.Z) >= (large ? LargeDoodadSlotClearance : DoodadSlotClearance),
                        $"{piece.Name}: {slot} is {blocker.DistanceTo(slot.X, slot.Z):0.00} m from {blocker}");
                }
                foreach (Slot spawn in piece.Slots)
                {
                    float d = MathF.Sqrt((slot.X - spawn.X) * (slot.X - spawn.X) + (slot.Z - spawn.Z) * (slot.Z - spawn.Z));
                    Assert.True(d >= (large ? LargeDoodadSpawnClearance : DoodadSpawnClearance), $"{piece.Name}: {slot} is {d:0.00} m from {spawn}");
                }
                foreach (Side side in piece.Exits)
                {
                    (float minX, float maxX, float minZ, float maxZ) = Throat(side);
                    Assert.False(slot.X >= minX && slot.X <= maxX && slot.Z >= minZ && slot.Z <= maxZ, $"{piece.Name}: {slot} stands in its {side} exit");
                    float d = DistanceToSegment(slot.X, slot.Z, (minX + maxX) / 2f, (minZ + maxZ) / 2f, ChunkPiece.CellSize / 2f, ChunkPiece.CellSize / 2f);
                    Assert.True(d >= (large ? LargeDoodadPathClearance : DoodadPathClearance), $"{piece.Name}: {slot} is {d:0.00} m from its {side} path");
                }
            }
        }
    }

    /// <summary>An exit's throat (10 m wide, 6 m deep, centred on its side) holds no blocker.</summary>
    [Fact]
    public void Keep_every_exit_throat_clear()
    {
        foreach (ChunkPiece piece in Pieces())
        {
            foreach (Side side in piece.Exits)
            {
                (float minX, float maxX, float minZ, float maxZ) = Throat(side);
                foreach (Blocker blocker in piece.Blockers)
                {
                    (float MinX, float MaxX, float MinZ, float MaxZ) b = blocker.Bounds;
                    bool overlaps = b.MinX < maxX && b.MaxX > minX && b.MinZ < maxZ && b.MaxZ > minZ;
                    Assert.False(overlaps, $"{piece.Name}: {blocker} stands in its {side} exit");
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Singles))]
    public void Stand_every_slot_on_the_navmesh_floor_of_a_piece(string name)
    {
        ChunkPiece piece = ForestPieces.Singles().Single(p => p.Name == name);
        MapNavigator navigator = GeneratedChunkBake.Bake([(ObjWriter.Write(piece), 0, 0)]);

        foreach ((float x, float z) in piece.Slots.Select(s => (s.X, s.Z)).Concat(piece.DoodadSlots.Select(s => (s.X, s.Z))))
        {
            Assert.Equal(NavmeshGroundKind.Under, navigator.FindGround(new Vector3(x, 1f, z), out Vector3 ground));
            Assert.InRange(ground.y, -0.3f, 0.3f);   // the floor, never a blocker's top
        }
    }

    [Theory]
    [MemberData(nameof(Singles))]
    public void Walk_between_every_exit_and_every_slot_of_a_piece(string name)
    {
        ChunkPiece piece = ForestPieces.Singles().Single(p => p.Name == name);
        MapNavigator navigator = GeneratedChunkBake.Bake([(ObjWriter.Write(piece), 0, 0)]);
        List<Vector3> targets = [.. piece.Exits.Select(e => ExitPoint(e, 0, 0)), .. piece.Slots.Select(s => new Vector3(s.X, 1f, s.Z))];

        AssertAllConnected(navigator, targets, name);
    }

    /// <summary>The four members baked together, as the generator places them unrotated: their inner edges must be open.</summary>
    [Theory]
    [MemberData(nameof(Groups))]
    public void Walk_across_a_set_pieces_open_inner_edges(string name)
    {
        ChunkGroupPiece group = ForestPieces.Groups().Single(g => g.Name == name);
        var members = group.Members().ToList();
        MapNavigator navigator = GeneratedChunkBake.Bake(members.Select(m => (ObjWriter.Write(m.Piece), m.CellX, m.CellZ)));

        List<Vector3> targets = [.. group.OuterExits.Select(e => ExitPoint(e.Side, e.CellX, e.CellZ)), .. group.Slots.Select(s => new Vector3(s.X, 1f, s.Z))];
        foreach ((float x, float z) in group.Slots.Select(s => (s.X, s.Z)).Concat(group.DoodadSlots.Select(s => (s.X, s.Z))))
        {
            Assert.Equal(NavmeshGroundKind.Under, navigator.FindGround(new Vector3(x, 1f, z), out Vector3 ground));
            Assert.InRange(ground.y, -0.3f, 0.3f);
        }
        AssertAllConnected(navigator, targets, name);
    }

    [Fact]
    public void Carry_a_boss_only_in_the_arena_and_leaders_in_every_set_piece()
    {
        Assert.All(ForestPieces.Singles(), p => Assert.DoesNotContain(p.Slots, s => s.Tag is "boss" or "leader"));
        Assert.Single(ForestPieces.Groups().Single(g => g.Name == "forest_arena").Slots, s => s.Tag == "boss");
        Assert.All(ForestPieces.Groups(), g => Assert.True(g.Slots.Count(s => s.Tag == "leader") >= 2, $"{g.Name} has fewer than two leaders"));
    }

    /// <summary>chunk-groups.json lists exactly the generated set pieces and their cells; chunk-pools.json lists every single piece and no member.</summary>
    [Fact]
    public void List_the_pieces_in_the_pool_and_group_files()
    {
        using var pools = JsonDocument.Parse(File.ReadAllText(Path.Combine(Maps, "chunk-pools.json")));
        var forest = pools.RootElement.GetProperty("forest_pool").EnumerateArray().Select(e => e.GetString()!).ToList();
        Assert.All(ForestPieces.Singles(), p => Assert.Contains(p.Name, forest));
        Assert.DoesNotContain(forest, n => ForestPieces.Groups().Any(g => n.StartsWith(g.Name + "_", StringComparison.Ordinal)));
        Assert.DoesNotContain("forest_boss_01", forest);   // owner decision 5

        using var groups = JsonDocument.Parse(File.ReadAllText(Path.Combine(Maps, "chunk-groups.json")));
        var listed = groups.RootElement.GetProperty("forest_pool").EnumerateArray().ToDictionary(
            g => g.GetProperty("name").GetString()!,
            g => g.GetProperty("members").EnumerateArray()
                .Select(m => (m.GetProperty("chunk").GetString()!, m.GetProperty("cellX").GetInt32(), m.GetProperty("cellZ").GetInt32()))
                .OrderBy(m => m.Item1, StringComparer.Ordinal).ToList());
        Assert.Equal(ForestPieces.Groups().Select(g => g.Name).Order(), listed.Keys.Order());
        foreach (ChunkGroupPiece group in ForestPieces.Groups())
            Assert.Equal(group.Members().Select(m => (m.Piece.Name, m.CellX, m.CellZ)).OrderBy(m => m.Name, StringComparer.Ordinal), listed[group.Name]);
    }

    /// <summary>An exit's throat: 10 m wide and 6 m deep, centred on its side.</summary>
    private static (float MinX, float MaxX, float MinZ, float MaxZ) Throat(Side side) => side switch
    {
        Side.N => (10f, 20f, 24f, 30f),
        Side.S => (10f, 20f, 0f, 6f),
        Side.E => (24f, 30f, 10f, 20f),
        _ => (0f, 6f, 10f, 20f),
    };

    private static float DistanceToSegment(float px, float pz, float ax, float az, float bx, float bz)
    {
        float dx = bx - ax, dz = bz - az;
        float t = Math.Clamp(((px - ax) * dx + (pz - az) * dz) / (dx * dx + dz * dz), 0f, 1f);
        float cx = ax + t * dx - px, cz = az + t * dz - pz;
        return MathF.Sqrt(cx * cx + cz * cz);
    }

    /// <summary>A point just inside the middle of a side: the floor's edge is eroded by the agent radius (0.6 m).</summary>
    private static Vector3 ExitPoint(Side side, int cellX, int cellZ)
    {
        float ox = cellX * 30f, oz = cellZ * 30f;
        return side switch
        {
            Side.N => new Vector3(ox + 15f, 1f, oz + 29f),
            Side.S => new Vector3(ox + 15f, 1f, oz + 1f),
            Side.E => new Vector3(ox + 29f, 1f, oz + 15f),
            _ => new Vector3(ox + 1f, 1f, oz + 15f),
        };
    }

    private static void AssertAllConnected(MapNavigator navigator, List<Vector3> targets, string name)
    {
        Vector3 from = targets[0];
        foreach (Vector3 to in targets.Skip(1))
        {
            List<Vector3> path = navigator.FindPath(from, to);
            Assert.NotEmpty(path);
            Vector3 end = path[^1];
            float gap = MathF.Sqrt((end.x - to.x) * (end.x - to.x) + (end.z - to.z) * (end.z - to.z));
            Assert.True(gap <= 1f, $"{name}: the walk from {from} to {to} ends {gap:0.00} m short");
        }
    }

    /// <summary>Every piece the tool writes: the singles and each set piece's four members.</summary>
    private static List<ChunkPiece> Pieces() =>
        [.. ForestPieces.Singles(), .. ForestPieces.Groups().SelectMany(g => g.Members().Select(m => m.Piece))];

    private static string Lf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Avalon.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("No Avalon.sln above the test output.");
    }
}
