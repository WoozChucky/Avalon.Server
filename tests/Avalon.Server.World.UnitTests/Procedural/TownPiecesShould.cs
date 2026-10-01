using Avalon.ChunkGen;
using Avalon.Common.Mathematics;
using Avalon.World.Maps.Navigation;
using Xunit;

namespace Avalon.Server.World.UnitTests.Procedural;

/// <summary>
/// Glimmerdell's four squares (town beautification, 2026-10-01): what is committed is what the tool writes, today's
/// floor and walls are reproduced, and the owner's rules hold: nothing within 2 m of a wall, the doorway lanes and the
/// arrival-to-portal corridor clear, 2.5 m of headroom under every roof, risers within the navmesh step, and no prop
/// top that the baked navmesh climbs.
/// </summary>
public class TownPiecesShould
{
    private static string Maps => Path.Combine(RepositoryRoot(), "src", "Server", "Avalon.Server.World", "Maps");

    private static readonly Lazy<MapNavigator> Town = new(
        () => GeneratedChunkBake.Bake(TownPieces.Squares().Select(s => (ObjWriter.Write(s), s.GridX, s.GridZ))), isThreadSafe: true);

    [Fact]
    public void Match_the_committed_files()
    {
        foreach ((string name, string obj, string json) in ChunkFiles.For(TownPieces.Squares()))
        {
            Assert.Equal(obj, Lf(File.ReadAllText(Path.Combine(Maps, "Chunks", name + ".obj"))));
            Assert.Equal(json, Lf(File.ReadAllText(Path.Combine(Maps, "Chunks", name + ".json"))));
        }
    }

    [Fact]
    public void Lay_the_four_squares_out_as_the_town_layout_does()
    {
        Assert.Equal([("town_sw_01", 0, 0), ("town_se_01", 1, 0), ("town_nw_01", 0, 1), ("town_ne_01", 1, 1)],
            TownPieces.Squares().Select(s => (s.Name, s.GridX, s.GridZ)));
        Assert.Equal([Side.N, Side.E], TownPieces.Squares()[0].Exits);
        Assert.Equal([Side.N, Side.W], TownPieces.Squares()[1].Exits);
        Assert.Equal([Side.E, Side.S], TownPieces.Squares()[2].Exits);
        Assert.Equal([Side.S, Side.W], TownPieces.Squares()[3].Exits);
        Assert.Equal("town_sw_01", Assert.Single(TownPieces.Squares(), s => s.IsEntry).Name);
        Assert.Equal("town_nw_01", Assert.Single(TownPieces.Squares(), s => s.HasForwardPortal).Name);
    }

    /// <summary>Today's wall boxes, by name, exactly (the Unity exporter's extents; Y is 0-2 for every wall).</summary>
    [Theory]
    [InlineData("town_sw_01", "Wall_N_L", 0f, 12f, 29.75f, 30.25f)]
    [InlineData("town_sw_01", "Wall_N_R", 18f, 30f, 29.75f, 30.25f)]
    [InlineData("town_sw_01", "Wall_E_L", 29.75f, 30.25f, 0f, 12f)]
    [InlineData("town_sw_01", "Wall_E_R", 29.75f, 30.25f, 18f, 30f)]
    [InlineData("town_sw_01", "Wall_S", 0f, 30f, -0.25f, 0.25f)]
    [InlineData("town_sw_01", "Wall_W", -0.25f, 0.25f, 0f, 30f)]
    [InlineData("town_se_01", "Wall_N_L", 0f, 12f, 29.75f, 30.25f)]
    [InlineData("town_se_01", "Wall_N_R", 18f, 30f, 29.75f, 30.25f)]
    [InlineData("town_se_01", "Wall_E", 29.75f, 30.25f, 0f, 30f)]
    [InlineData("town_se_01", "Wall_S", 0f, 30f, -0.25f, 0.25f)]
    [InlineData("town_se_01", "Wall_W_L", -0.25f, 0.25f, 0f, 12f)]
    [InlineData("town_se_01", "Wall_W_R", -0.25f, 0.25f, 18f, 30f)]
    [InlineData("town_nw_01", "Wall_N", 0f, 30f, 29.75f, 30.25f)]
    [InlineData("town_nw_01", "Wall_E_L", 29.75f, 30.25f, 0f, 12f)]
    [InlineData("town_nw_01", "Wall_E_R", 29.75f, 30.25f, 18f, 30f)]
    [InlineData("town_nw_01", "Wall_S_L", 0f, 12f, -0.25f, 0.25f)]
    [InlineData("town_nw_01", "Wall_S_R", 18f, 30f, -0.25f, 0.25f)]
    [InlineData("town_nw_01", "Wall_W", -0.25f, 0.25f, 0f, 30f)]
    [InlineData("town_ne_01", "Wall_N", 0f, 30f, 29.75f, 30.25f)]
    [InlineData("town_ne_01", "Wall_E", 29.75f, 30.25f, 0f, 30f)]
    [InlineData("town_ne_01", "Wall_S_L", 0f, 12f, -0.25f, 0.25f)]
    [InlineData("town_ne_01", "Wall_S_R", 18f, 30f, -0.25f, 0.25f)]
    [InlineData("town_ne_01", "Wall_W_L", -0.25f, 0.25f, 0f, 12f)]
    [InlineData("town_ne_01", "Wall_W_R", -0.25f, 0.25f, 18f, 30f)]
    public void Reproduce_todays_walls(string square, string wall, float minX, float maxX, float minZ, float maxZ)
    {
        TownSquare s = TownPieces.Squares().Single(q => q.Name == square);
        WallSegment w = Assert.Single(s.Walls, x => x.Name == wall);
        Assert.Equal((minX, maxX, minZ, maxZ), (w.MinX, w.MaxX, w.MinZ, w.MaxZ));
        Assert.Equal(6, s.Walls.Count);
    }

    [Fact]
    public void Keep_every_rule_of_the_layout() => Assert.All(TownPieces.Squares(), s => s.Validate());

    [Fact]
    public void Tag_every_piece_with_a_known_material() =>
        Assert.All(TownPieces.Squares().SelectMany(s => s.Pieces), p => Assert.True(Enum.IsDefined(p.Material)));

    [Fact]
    public void Carry_the_approved_buildings()
    {
        string[] buildings = TownPieces.Squares().SelectMany(s => s.Pieces).Select(p => p.Building).Distinct().Order().ToArray();
        Assert.Contains("Town hall", buildings);
        Assert.Contains("Fountain", buildings);
        Assert.Contains("Gate arch", buildings);
        Assert.Contains("Watchtower", buildings);
        Assert.Contains("Hunter's lodge", buildings);
        Assert.Contains("Smithy", buildings);
        Assert.Contains("Armourer's stall", buildings);
        Assert.Contains("General-goods stall", buildings);
        Assert.Contains("Bank", buildings);
        Assert.Contains("Inn", buildings);
        Assert.Contains("House A", buildings);
        Assert.Contains("House B", buildings);
        Assert.Contains("Well", buildings);
        Assert.Equal(112, TownPieces.Squares().Sum(s => s.Pieces.Count));
    }

    /// <summary>Owner decision 2: the two stalls are told apart by their cloth.</summary>
    [Fact]
    public void Give_each_stall_its_own_cloth()
    {
        List<TownPiece> market = [.. TownPieces.Squares().Single(s => s.Name == "town_se_01").Pieces];
        Assert.Equal(Material.Cloth, market.Single(p => p.Building == "Armourer's stall" && p.Part == "roof").Material);
        Assert.Equal(Material.Cloth2, market.Single(p => p.Building == "General-goods stall" && p.Part == "awning").Material);
    }

    [Fact]
    public void Reach_the_portal_from_the_arrival_point()
    {
        var arrival = new Vector3(TownPieces.ArrivalX, 1f, TownPieces.ArrivalZ);
        var portal = new Vector3(TownPieces.PortalX, 1f, TownPieces.PortalZ);
        Assert.Equal(NavmeshGroundKind.Under, Town.Value.FindGround(arrival, out _));
        Assert.Equal(NavmeshGroundKind.Under, Town.Value.FindGround(portal, out _));

        List<Vector3> path = Town.Value.FindPath(arrival, portal);
        Assert.NotEmpty(path);
        Vector3 end = path[^1];
        Assert.True(MathF.Abs(end.x - portal.x) <= 1f && MathF.Abs(end.z - portal.z) <= 1f, $"the path ends at {end}");

        // The corridor itself: a straight walk north along x = 15 through the doorway and under the arch.
        Vector3 stop = Town.Value.RaycastWalkable(new Vector3(15f, 0.15f, 15f), new Vector3(15f, 0.15f, 45f));
        Assert.True(stop.z >= 44.5f, $"the walk north along x = 15 stopped at z = {stop.z}");
    }

    /// <summary>
    /// Recast climbs any step of 0.8 m or less (floor(0.9 / 0.2) voxels) and a span top rounds up to a 0.2 m voxel, so a
    /// prop whose top is above 1.0 m is solid to movement and one at or below it is a step the navmesh walks over
    /// (benches, crates, barrels, counters, the well ring). Every solid prop and building must stop a walk at its
    /// edge: a walkable ray from 1.5 m west of its footprint toward its centre never enters it (the fountain basin
    /// did at 0.8 m; the owner raised it to 1.1 m, decision 1). A ray that starts inside another solid stops where it
    /// starts and proves nothing, which is accepted: a prop wedged against a building is solid by its neighbour.
    /// </summary>
    [Fact]
    public void Stop_a_walk_at_the_edge_of_every_solid_piece()
    {
        var entered = new List<string>();
        foreach (TownSquare square in TownPieces.Squares())
        {
            (float ox, float oz) = square.Origin;
            foreach (TownPiece piece in square.Pieces.Where(p => !p.Walkable && p.Y0 <= 0.05f && p.Top > 1.0f))
            {
                (float minX, _, _, _) = piece.Bounds;
                (float cx, float cz) = piece.Centre;
                var from = new Vector3(ox + minX - 1.5f, 0.15f, oz + cz);
                var to = new Vector3(ox + cx, 0.15f, oz + cz);
                Vector3 stop = Town.Value.RaycastWalkable(from, to);
                if (stop.x > ox + minX + 0.05f)
                    entered.Add($"{square.Name}: {piece.Building}/{piece.Part} entered to x = {stop.x:0.00} (edge {ox + minX:0.00})");
            }
        }
        Assert.True(entered.Count == 0, "a walk entered: " + string.Join("; ", entered));
    }

    /// <summary>The rule above, stated once for the data: no solid prop sits in the 1.0-1.05 m band where the voxel maths is a coin toss.</summary>
    [Fact]
    public void Keep_every_solid_piece_clear_of_the_navmesh_step_band() =>
        Assert.All(TownPieces.Squares().SelectMany(s => s.Pieces).Where(p => !p.Walkable && p.Y0 <= 0.05f),
            p => Assert.True(p.Top <= 1.0f || p.Top >= 1.05f, $"{p.Building}/{p.Part} top {p.Top}"));

    private static string Lf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Avalon.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("No Avalon.sln above the test output.");
    }
}
