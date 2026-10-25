using Avalon.ChunkGen;
using Avalon.Common.Mathematics;
using Avalon.World.ChunkLayouts;
using Avalon.World.Maps.Navigation;

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

    private static readonly Lazy<MapNavigator> s_town = new(
        () => GeneratedChunkBake.Bake(TownPieces.Squares().Select(s => (ObjWriter.Write(s), s.GridX, s.GridZ))), isThreadSafe: true);

    [Fact]
    public void Match_the_committed_files()
    {
        foreach ((string name, string obj, string json, string? decor) in ChunkFiles.For(TownPieces.Squares()))
        {
            Assert.Equal(obj, Lf(File.ReadAllText(Path.Combine(Maps, "Chunks", name + ".obj"))));
            Assert.Equal(json, Lf(File.ReadAllText(Path.Combine(Maps, "Chunks", name + ".json"))));
            Assert.Equal(decor, Lf(File.ReadAllText(Path.Combine(Maps, "Decor", name + ".json"))));
        }

        SceneryFiles scenery = ChunkFiles.TownScenery(TownScenery.Pieces());
        foreach ((string name, string obj, string decor) in scenery.Pieces)
        {
            Assert.Equal(obj, Lf(File.ReadAllText(Path.Combine(Maps, "Scenery", name + ".obj"))));
            Assert.Equal(decor, Lf(File.ReadAllText(Path.Combine(Maps, "Decor", name + ".json"))));
        }
        Assert.Equal(scenery.Ring, Lf(File.ReadAllText(Path.Combine(Maps, "Scenery", $"{ChunkFiles.TownMapId}.json"))));
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
    [InlineData("town_nw_01", "Wall_N_L", 0f, 12f, 29.75f, 30.25f)]   // the gate's opening at X 12-18
    [InlineData("town_nw_01", "Wall_N_R", 18f, 30f, 29.75f, 30.25f)]
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
        Assert.Equal(s.Gate is null ? 6 : 7, s.Walls.Count);   // a gate splits its wall in two
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
        Assert.Contains("Torches", buildings);
        Assert.Contains("Braziers", buildings);
        Assert.DoesNotContain("Lamp posts", buildings);
        Assert.Equal(116, TownPieces.Squares().Sum(s => s.Pieces.Count));
    }

    /// <summary>
    /// The client's camera is fixed at yaw 45 (owner decision, 2026-10-02), south-west of what it looks at: a front on
    /// the +X or +Z side shows the player a back wall, with the porch and its NPC behind the building.
    /// </summary>
    /// <summary>
    /// The red-brick roads are the routes a player runs along: no free-standing piece they would bump into (below body
    /// height, not part of a building) stands on one. Buildings stay where they are, the gate arch over its road included.
    /// </summary>
    [Fact]
    public void Keep_every_road_clear_of_props()
    {
        string[] onRoads =
        [
            .. TownPieces.Squares().SelectMany(s => s.Pieces
                .Where(p => !p.Walkable && !p.PartOfBuilding && p.Y0 < DecorRules.BodyHeight &&
                            TownSquare.OverlapsRoad(p.Bounds.MinX, p.Bounds.MaxX, p.Bounds.MinZ, p.Bounds.MaxZ, s.Exits))
                .Select(p => $"{s.Name} {p.Building}/{p.Part}")),
        ];
        Assert.True(onRoads.Length == 0, "on a road: " + string.Join(", ", onRoads));
    }

    [Fact]
    public void Face_every_building_front_towards_the_camera()
    {
        List<(string Square, TownPiece Piece)> fronts =
            [.. TownPieces.Squares().SelectMany(s => s.Pieces.Where(p => p.Front != Facing.None).Select(p => (s.Name, p)))];
        Assert.Equal(11, fronts.Count);
        Assert.All(fronts, f => Assert.True(f.Piece.Front is Facing.NegX or Facing.NegZ,
            $"{f.Square}: {f.Piece.Building}'s front faces {f.Piece.Front}, away from the camera"));
    }

    /// <summary>Owner decision 2: the two stalls are told apart by their cloth, red and blue (their kit-textured models).</summary>
    [Fact]
    public void Give_each_stall_its_own_cloth()
    {
        List<DecorItem> market = [.. TownPieces.Squares().Single(s => s.Name == "town_se_01").Decor];
        Assert.Equal("avalon_stall_armourer", market.Single(d => d.Building == "Armourer's stall").Model);
        Assert.Equal("avalon_stall_goods", market.Single(d => d.Building == "General-goods stall").Model);
    }

    /// <summary>
    /// The way out of town: the portal square's red-brick road runs north to a gate in its wall (an opening, not a
    /// doorway to another square), and the portal stands in that gate.
    /// </summary>
    [Fact]
    public void Lead_the_road_to_the_gate_and_put_the_portal_in_it()
    {
        TownSquare square = TownPieces.Squares().Single(s => s.HasForwardPortal);
        Assert.Equal(Side.N, square.Gate);
        Assert.DoesNotContain(Side.N, square.Exits);
        Assert.DoesNotContain(square.Walls, w => w.Name == "Wall_N");
        Assert.True(TownSquare.OnRoad(15f, 29f, square.RoadSides), "the road does not reach the gate");
        (float ox, float oz) = square.Origin;
        Assert.InRange(TownPieces.PortalZ - oz, 27.5f, 29.5f);
        Assert.Equal(15f, TownPieces.PortalX - ox);
    }

    [Fact]
    public void Reach_the_portal_from_the_arrival_point()
    {
        var arrival = new Vector3(TownPieces.ArrivalX, 1f, TownPieces.ArrivalZ);
        var portal = new Vector3(TownPieces.PortalX, 1f, TownPieces.PortalZ);
        Assert.Equal(NavmeshGroundKind.Under, s_town.Value.FindGround(arrival, out _));
        Assert.Equal(NavmeshGroundKind.Under, s_town.Value.FindGround(portal, out _));

        List<Vector3> path = s_town.Value.FindPath(arrival, portal);
        Assert.NotEmpty(path);
        Vector3 end = path[^1];
        Assert.True(MathF.Abs(end.x - portal.x) <= 1f && MathF.Abs(end.z - portal.z) <= 1f, $"the path ends at {end}");

        // The corridor itself: a straight walk north along x = 15 through the doorway, up the road and into the gate.
        Vector3 stop = s_town.Value.RaycastWalkable(new Vector3(15f, 0.15f, 15f), new Vector3(15f, 0.15f, TownPieces.PortalZ));
        Assert.True(stop.z >= TownPieces.PortalZ - 0.5f, $"the walk north along x = 15 stopped at z = {stop.z}");
    }

    /// <summary>Every solid piece: non-walkable and standing below 2 m (roof slabs and gables above that are never walked against).</summary>
    private static IEnumerable<(TownSquare Square, TownPiece Piece)> SolidPieces() =>
        TownPieces.Squares().SelectMany(s => s.Pieces.Where(p => !p.Walkable && p.Y0 < TownRules.SolidBelow).Select(p => (s, p)));

    /// <summary>
    /// Recast climbs any step of 0.8 m or less (floor(0.9 / 0.2) voxels) and a span top rounds up to a 0.2 m voxel, so a
    /// top at or under 1.0 m above the ground beside it would be a step the navmesh walks over. Every solid piece,
    /// floating ones included (the fountain's water and upper bowl), must stop a walk at its edge: a walkable ray from
    /// 1.5 m west of its footprint toward its centre never enters it (owner decision 8 raised every low prop to 1.05 m;
    /// a lamp post's or a stall's 0.14-0.2 m post stops it too). A ray that starts inside another solid stops where it
    /// starts and proves nothing, which is accepted: a prop wedged against a building is solid by its neighbour.
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
            Vector3 stop = s_town.Value.RaycastWalkable(from, to);
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
            List<Vector3> path = s_town.Value.FindPath(arrival, new Vector3(ox + cx, piece.Top + 0.3f, oz + cz));
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
        Vector3 stop = s_town.Value.RaycastWalkable(from, new Vector3(basin.X, 0.15f, basin.Z));
        Assert.True(stop.x <= basin.X - basin.Radius + 0.05f, $"the walk entered the basin to x = {stop.x:0.00}");
        List<Vector3> path = s_town.Value.FindPath(new Vector3(TownPieces.ArrivalX, 1f, TownPieces.ArrivalZ), new Vector3(water.X + 1f, water.Top + 0.3f, water.Z));
        if (path.Count > 0)
            Assert.True(water.DistanceTo(path[^1].x, path[^1].z) > 0.05f || path[^1].y < water.Y0 + 0.1f, $"a path reached the water at {path[^1]}");
    }

    /// <summary>
    /// Each scenery rule refuses the town ring that breaks it (scenery ring, #1110): the 21 flat cut cells pass as built
    /// (SceneryTestPieces), then one thing is broken.
    /// </summary>
    [Theory]
    [InlineData("an item across a cell edge", "crosses the edge of town_ring_3_1")]
    [InlineData("a cart by the south wall", "taller than 2 m within 4 m of the wall")]
    [InlineData("a house near the west wall", "taller than 6 m within 10 m of the wall")]
    [InlineData("a big tree by the north wall", "within 6 m of an edge")]
    [InlineData("a ring cell left out", "no piece for ring cell (3, 3)")]
    [InlineData("a step against the town", "meets the town at y 0.4")]
    [InlineData("a seam between ring cells", "do not meet")]
    [InlineData("dirt laid under the ground", "m under the ground: it would not show")]
    public void Refuse_town_scenery_that_breaks_a_scenery_rule(string broken, string refusal)
    {
        List<SceneryPiece> pieces = [.. SceneryTestPieces.Town()];
        ChunkFiles.TownScenery(pieces);   // as built, it passes

        int south = pieces.FindIndex(p => p.Name == "town_ring_0_m1");
        int west = pieces.FindIndex(p => p.Name == "town_ring_m1_0");
        Action check = () => ChunkFiles.TownScenery(pieces);
        switch (broken)
        {
            case "an item across a cell edge":
                check = () => SceneryComposer.Cut("town_ring", SceneryRing.Cells(SceneryTestPieces.TownCells), SceneryTestPieces.Flat("meadow"),
                    [new DecorItem("", "Barrel", 90f, 0f, 45f, 0)], DecorJsonWriter.TownKit);
                break;
            case "a cart by the south wall":
            {
                // A wagon scaled to 3.1 m tall, 2 m south of the wall (the town's z = 0, this cell's local z = 30).
                ModelBounds wagon = KitBounds.Load()["Prop_Wagon"];
                pieces[south] = pieces[south] with { Decor = [new DecorItem("", "Prop_Wagon", 15f, 0f, 28f, 0, 3.1f / wagon.MaxY)] };
                break;
            }
            case "a house near the west wall":
            {
                // The house's box 7 m west of the wall (the town's x = 0, this cell's local x = 30): past the 4 m rule, inside the 10 m one.
                var house = BuildingRecipe.Load("house_a");
                float reach = TownBuilding.Place(house, "house_a", 0f, 0f, 0).Decor.Max(d => DecorRules.WorldBounds(d).MaxX);
                IReadOnlyList<DecorItem> decor = TownBuilding.Place(house, "house_a", 30f - 7f - reach, 15f, 0).Decor;
                Assert.True(decor.Max(d => DecorRules.WorldBounds(d).MaxY) > SceneryRules.TownWallFarTop, "house_a is drawn no taller than 6 m");
                pieces[west] = pieces[west] with { Decor = decor };
                break;
            }
            case "a big tree by the north wall":
            {
                // A nature-kit tree at twice the tree walls' scale 5 m north of the wall, its box inside the cell (at 3 m a
                // scale-2 crown would reach past it): on an edge shared with the town only the edge band applies, since
                // the camera-side rules cover the south and west cells alone.
                int north = pieces.FindIndex(p => p.Name == "town_ring_0_2");
                pieces[north] = pieces[north] with { Decor = [new DecorItem("", "CommonTree_1", 15f, 0f, 5f, 0, 2f, DecorJsonWriter.ForestKit)] };
                break;
            }
            case "a ring cell left out":
                pieces.RemoveAll(p => (p.GridX, p.GridZ) == (3, 3));
                break;
            case "a step against the town":
                pieces = [.. SceneryTestPieces.Town(new SceneryTerrain((x, _) => x == 0f ? 0.4f : 0f, (_, _) => "meadow"))];
                break;
            case "a seam between ring cells":
            {
                // The corner cell (3, 3) has no edge on the town: only its seams with the ring cells beside it break.
                int corner = pieces.FindIndex(p => (p.GridX, p.GridZ) == (3, 3));
                pieces[corner] = pieces[corner] with { Terrain = new SceneryTerrain((_, _) => 0.2f, (_, _) => "meadow") };
                break;
            }
            case "dirt laid under the ground":
            {
                // Dirt 5 cm up over a cell whose ground rises 0.5 m at one vertex inside it (its seams untouched).
                int corner = pieces.FindIndex(p => (p.GridX, p.GridZ) == (3, 3));
                pieces[corner] = pieces[corner] with
                {
                    Terrain = new SceneryTerrain((x, z) => x == 15f && z == 15f ? 0.5f : 0f, (_, _) => "meadow")
                    {
                        Surfaces = [ScenerySurface.Area((x, z) => x > 14f && x < 17f && z > 14f && z < 17f, 0.05f, "dirt_road")],
                    },
                };
                break;
            }
        }

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(check);
        Assert.Contains(refusal, error.Message, StringComparison.Ordinal);
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
