using Avalon.ChunkGen;
using Avalon.Common.Mathematics;
using Avalon.World.Maps.Navigation;
using Xunit;

namespace Avalon.Server.World.UnitTests.Procedural;

/// <summary>
/// Glimmerdell's four squares (town beautification, 2026-10-01): what is committed is what the tool writes, today's
/// floor and walls are reproduced, and the owner's rules hold: nothing within 2 m of a wall, the doorway lanes and the
/// arrival-to-portal corridor clear, 2.5 m of headroom under every roof, risers within the navmesh step, and every
/// solid piece solid to movement on the baked navmesh.
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

    /// <summary>The town's wall boxes, by name, exactly (Y is 0-2 for every wall).</summary>
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

    /// <summary>
    /// The client's camera is fixed at yaw 45 (owner decision, 2026-10-02), south-west of what it looks at: a front on
    /// the +X or +Z side shows the player a back wall, with the porch and its NPC behind the building.
    /// </summary>
    [Fact]
    public void Face_every_building_front_towards_the_camera()
    {
        List<(string Square, TownPiece Piece)> fronts =
            [.. TownPieces.Squares().SelectMany(s => s.Pieces.Where(p => p.Front != Facing.None).Select(p => (s.Name, p)))];
        Assert.Equal(7, fronts.Count);
        Assert.All(fronts, f => Assert.True(f.Piece.Front is Facing.NegX or Facing.NegZ,
            $"{f.Square}: {f.Piece.Building}'s front faces {f.Piece.Front}, away from the camera"));
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

    /// <summary>Every solid piece: non-walkable and standing below 2 m (roof slabs and gables above that are never walked against).</summary>
    private static IEnumerable<(TownSquare Square, TownPiece Piece)> SolidPieces() =>
        TownPieces.Squares().SelectMany(s => s.Pieces.Where(p => !p.Walkable && p.Y0 < TownRules.SolidBelow).Select(p => (s, p)));

    /// <summary>
    /// Recast climbs any step of 0.8 m or less (floor(0.9 / 0.2) voxels) and a span top rounds up to a 0.2 m voxel, so a
    /// top at or under 1.0 m above the ground beside it would be a step the navmesh walks over. Every solid piece,
    /// floating ones included (the cart's bed, the stacked crate), must stop a walk at its edge: a walkable ray from
    /// 1.5 m west of its footprint toward its centre never enters it (owner decision 8 raised every low prop to 1.05 m
    /// and put the cart's shaft on the floor). A ray that starts inside another solid stops where it starts and proves
    /// nothing, which is accepted: a prop wedged against a building is solid by its neighbour.
    /// </summary>
    [Fact]
    public void Stop_a_walk_at_the_edge_of_every_solid_piece()
    {
        var entered = new List<string>();
        foreach ((TownSquare square, TownPiece piece) in SolidPieces())
        {
            (float ox, float oz) = square.Origin;
            (float minX, _, _, _) = piece.Bounds;
            (float cx, float cz) = piece.Centre;
            var from = new Vector3(ox + minX - 1.5f, 0.15f, oz + cz);
            var to = new Vector3(ox + cx, 0.15f, oz + cz);
            Vector3 stop = Town.Value.RaycastWalkable(from, to);
            if (stop.x > ox + minX + 0.05f)
                entered.Add($"{square.Name}: {piece.Building}/{piece.Part} entered to x = {stop.x:0.00} (edge {ox + minX:0.00})");
        }
        Assert.True(entered.Count == 0, "a walk entered: " + string.Join("; ", entered));
    }

    /// <summary>
    /// Stronger than the rays, which skip a piece wedged against another: no solid piece's top is reachable from the
    /// arrival point. A path to the point just above a piece's top centre ends on the floor beside it (or short of
    /// it), never on the piece: a top within the climb of a reachable surface, a bench, a floating bar, a crate on a
    /// crate, would end the path on itself. Roof tops and building interiors are navmesh islands nothing reaches.
    /// </summary>
    [Fact]
    public void Reach_no_solid_pieces_top_from_the_arrival_point()
    {
        var arrival = new Vector3(TownPieces.ArrivalX, 1f, TownPieces.ArrivalZ);
        var reached = new List<string>();
        foreach ((TownSquare square, TownPiece piece) in SolidPieces())
        {
            (float ox, float oz) = square.Origin;
            (float cx, float cz) = piece.Centre;
            List<Vector3> path = Town.Value.FindPath(arrival, new Vector3(ox + cx, piece.Top + 0.3f, oz + cz));
            if (path.Count == 0) continue;
            Vector3 end = path[^1];
            bool onTop = piece.DistanceTo(end.x - ox, end.z - oz) <= 0.05f && end.y > piece.Y0 + 0.1f;
            if (onTop)
                reached.Add($"{square.Name}: {piece.Building}/{piece.Part} reached at ({end.x:0.0}, {end.y:0.00}, {end.z:0.0})");
        }
        Assert.True(reached.Count == 0, "a path ends on: " + string.Join("; ", reached));
    }

    /// <summary>
    /// The rule above, stated once for the data: every solid piece's top is at least 1.05 m up, unless a ring whose rim
    /// is that high encloses it (the fountain's water inside its basin, whose surface the rim keeps off the plaza).
    /// </summary>
    [Fact]
    public void Keep_every_solid_pieces_top_above_the_navmesh_step()
    {
        foreach ((TownSquare square, TownPiece piece) in SolidPieces())
        {
            bool enclosed = square.Pieces.OfType<RingPiece>().Any(ring => !ReferenceEquals(ring, piece) && ring.Top >= 1.05f && ring.Encloses(piece));
            Assert.True(piece.Top >= 1.05f || enclosed, $"{square.Name}: {piece.Building}/{piece.Part} top {piece.Top}");
        }
    }

    /// <summary>The fountain reads as water in a basin: a rim the navmesh cannot climb, the water disc inside it, its surface below the rim.</summary>
    [Fact]
    public void Hold_the_fountains_water_inside_its_rim()
    {
        List<TownPiece> fountain = [.. TownPieces.Squares().Single(s => s.Name == "town_sw_01").Pieces.Where(p => p.Building == "Fountain")];
        RingPiece basin = Assert.IsType<RingPiece>(fountain.Single(p => p.Part == "basin"));
        CylinderPiece water = Assert.IsType<CylinderPiece>(fountain.Single(p => p.Part == "water"));

        Assert.True(basin.Top >= 1.05f, "the rim must stand above the navmesh step");
        Assert.InRange(basin.Radius - basin.InnerRadius, 0.3f, 0.41f);   // a 0.3-0.4 m rim (0.4 in single precision rounds just above)
        Assert.True(basin.Encloses(water), "the water must lie inside the rim");
        Assert.True(water.Top < basin.Top, "the water's surface must sit below the rim top");

        // Nothing walks onto the water from the plaza: the rim stops a walk, and the water's surface is an island.
        var from = new Vector3(basin.X - basin.Radius - 1.5f, 0.15f, basin.Z);
        Vector3 stop = Town.Value.RaycastWalkable(from, new Vector3(basin.X, 0.15f, basin.Z));
        Assert.True(stop.x <= basin.X - basin.Radius + 0.05f, $"the walk entered the basin to x = {stop.x:0.00}");
        List<Vector3> path = Town.Value.FindPath(new Vector3(TownPieces.ArrivalX, 1f, TownPieces.ArrivalZ), new Vector3(water.X + 1f, water.Top + 0.3f, water.Z));
        if (path.Count > 0)
            Assert.True(water.DistanceTo(path[^1].x, path[^1].z) > 0.05f || path[^1].y < water.Y0 + 0.1f, $"a path reached the water at {path[^1]}");
    }

    private static string Lf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Avalon.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("No Avalon.sln above the test output.");
    }
}
