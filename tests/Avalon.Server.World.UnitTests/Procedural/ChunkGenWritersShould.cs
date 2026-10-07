using System.Globalization;
using Avalon.ChunkGen;
using Avalon.Common.Mathematics;
using Avalon.Database.World.Seeding;
using Avalon.World.Maps.Navigation;

namespace Avalon.Server.World.UnitTests.Procedural;

public class ChunkGenWritersShould
{
    private static readonly ChunkPiece Sample = new("test_piece", [Side.N, Side.S],
        [new BoxBlocker(4, 10, 4, 10), new CylinderBlocker(22, 22, 2)],
        [new Slot("pack", 15, 15)], ["forest", "test"]);

    [Fact]
    public void Write_the_chunk_json_in_the_catalogs_layout()
    {
        string json = ChunkJsonWriter.Write(Sample.ToMeta());

        Assert.Equal("""
            {
              "name": "test_piece",
              "assetKey": "chunks/test_piece",
              "cellFootprintX": 1,
              "cellFootprintZ": 1,
              "cellSize": 30,
              "exits": {
                "N": ["center"],
                "E": [],
                "S": ["center"],
                "W": []
              },
              "spawnSlots": [
                { "tag": "pack", "localX": 15, "localY": 1, "localZ": 15 }
              ],
              "portalSlots": [
              ],
              "tags": ["forest", "test"]
            }

            """.Replace("\r\n", "\n", StringComparison.Ordinal), json);
    }

    [Fact]
    public void Write_portal_slots_in_the_catalogs_layout()
    {
        ChunkMetaDto meta = Sample.ToMeta() with { PortalSlots = [new PortalSlotDto("Forward", 15, 0, 15)] };

        string json = ChunkJsonWriter.Write(meta);

        Assert.Contains("""
              "portalSlots": [
                { "role": "Forward", "localX": 15, "localY": 0, "localZ": 15 }
              ],
            """.Replace("\r\n", "\n", StringComparison.Ordinal), json, StringComparison.Ordinal);
    }

    /// <summary>Every triangle faces out of its solid, so a top is walkable and a side is a wall (Recast reads (b-a)x(c-a)).</summary>
    [Fact]
    public void Wind_every_face_outward()
    {
        string obj = ObjWriter.Write(Sample);
        var vertices = new List<Vector3>();
        string current = "";
        var objects = new Dictionary<string, List<Vector3>>();
        var faces = new List<(string Object, int A, int B, int C)>();
        foreach (string line in obj.Split('\n'))
        {
            string[] p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (line.StartsWith("o ", StringComparison.Ordinal)) { current = p[1]; objects[current] = []; }
            else if (line.StartsWith("v ", StringComparison.Ordinal))
            {
                var v = new Vector3(float.Parse(p[1], CultureInfo.InvariantCulture), float.Parse(p[2], CultureInfo.InvariantCulture),
                    float.Parse(p[3], CultureInfo.InvariantCulture));
                vertices.Add(v);
                objects[current].Add(v);
            }
            else if (line.StartsWith("f ", StringComparison.Ordinal))
                faces.Add((current, int.Parse(p[1], CultureInfo.InvariantCulture) - 1, int.Parse(p[2], CultureInfo.InvariantCulture) - 1,
                    int.Parse(p[3], CultureInfo.InvariantCulture) - 1));
        }

        Assert.Equal(["Floor", "Blocker_1", "Blocker_2"], objects.Keys);
        foreach ((string name, int a, int b, int c) in faces)
        {
            List<Vector3> solid = objects[name];
            var centre = new Vector3(solid.Average(v => v.x), solid.Average(v => v.y), solid.Average(v => v.z));
            Vector3 u = vertices[b] - vertices[a], w = vertices[c] - vertices[a];
            var normal = new Vector3(u.y * w.z - u.z * w.y, u.z * w.x - u.x * w.z, u.x * w.y - u.y * w.x);
            Vector3 outward = (vertices[a] + vertices[b] + vertices[c]) / 3f - centre;
            Assert.True(normal.x * outward.x + normal.y * outward.y + normal.z * outward.z > 0, $"a face of {name} faces inward");
        }
    }

    [Fact]
    public void Bake_a_floor_that_a_blocker_cuts()
    {
        MapNavigator navigator = GeneratedChunkBake.Bake([(ObjWriter.Write(Sample), 0, 0)]);

        Assert.Equal(NavmeshGroundKind.Under, navigator.FindGround(new Vector3(15f, 1f, 15f), out Vector3 ground));
        Assert.InRange(ground.y, -0.3f, 0.3f);

        // A walk east along z = 22 stops at the cylinder (x 20-24), short of its centre.
        Vector3 stop = navigator.RaycastWalkable(new Vector3(14f, 0f, 22f), new Vector3(28f, 0f, 22f));
        Assert.True(stop.x < 20.1f, $"the walk went through the blocker to x = {stop.x}");
    }

    /// <summary>What the tool writes is what the World server's seeder reads: the same DTO, through the same reader.</summary>
    [Fact]
    public async Task Read_back_through_the_seeders_catalog_reader()
    {
        string root = Path.Combine(Path.GetTempPath(), $"avalon-chunkgen-{Guid.NewGuid():N}");
        try
        {
            CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Maps"), root);
            foreach ((string name, string obj, string json) in ChunkFiles.For([Sample]))
            {
                File.WriteAllText(Path.Combine(root, "Chunks", name + ".obj"), obj);
                File.WriteAllText(Path.Combine(root, "Chunks", name + ".json"), json);
            }

            ChunkCatalogFiles files = await ChunkCatalogSeeder.ReadCatalogAsync(root);

            ChunkMetaDto read = Assert.Single(files.Chunks, c => c.Name == "test_piece");
            Assert.Equal(["center"], read.Exits["N"]);
            Assert.Empty(read.Exits["E"]);
            Assert.Equal(new SpawnSlotDto("pack", 15, 1, 15), Assert.Single(read.SpawnSlots));
            Assert.Equal(["forest", "test"], read.Tags);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    internal static void CopyDirectory(string source, string target)
    {
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }

    [Fact]
    public void Split_a_group_into_members_in_their_own_cells()
    {
        var group = new ChunkGroupPiece("test_group", [(0, 0, Side.S), (1, 1, Side.N)],
            [new CylinderBlocker(45, 45, 2)], [new Slot("pack", 40, 10), new Slot("leader", 10, 40)], ["forest"]);

        List<(ChunkPiece Piece, int CellX, int CellZ)> members = group.Members().ToList();

        Assert.Equal(["test_group_sw", "test_group_se", "test_group_nw", "test_group_ne"], members.Select(m => m.Piece.Name));
        ChunkPiece ne = members.Single(m => m.CellX == 1 && m.CellZ == 1).Piece;
        Assert.Equal([Side.N], ne.Exits);
        Assert.Equal(new CylinderBlocker(15, 15, 2), Assert.Single(ne.Blockers));
        Assert.Equal(new Slot("pack", 10, 10), Assert.Single(members.Single(m => m.CellX == 1 && m.CellZ == 0).Piece.Slots));
        Assert.Contains("group", ne.Tags);
    }

    [Fact]
    public void Refuse_a_blocker_across_a_groups_inner_edge() =>
        Assert.Throws<InvalidOperationException>(() =>
            new ChunkGroupPiece("bad", [(0, 0, Side.S)], [new BoxBlocker(25, 35, 5, 10)], [], ["forest"]).Members().ToList());

    /// <summary>A bound that is not a number lands in no cell, so the blocker would be dropped without a word.</summary>
    [Theory]
    [InlineData(float.NaN, 10f)]
    [InlineData(10f, float.PositiveInfinity)]
    public void Refuse_a_group_blocker_whose_bounds_are_not_finite(float x, float z)
    {
        var refusal = Assert.Throws<InvalidOperationException>(() =>
            new ChunkGroupPiece("bad", [(0, 0, Side.S)], [new CylinderBlocker(x, z, 2)], [], ["forest"]).Members().ToList());
        Assert.Contains("not finite", refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(30f, 10f)]
    [InlineData(10f, 30f)]
    [InlineData(65f, 10f)]
    public void Refuse_a_group_slot_on_an_inner_edge_or_outside_the_frame(float x, float z) =>
        Assert.Throws<InvalidOperationException>(() =>
            new ChunkGroupPiece("bad", [(0, 0, Side.S)], [], [new Slot("pack", x, z)], ["forest"]).Members().ToList());

    [Fact]
    public void Refuse_a_group_blocker_outside_the_frame() =>
        Assert.Throws<InvalidOperationException>(() =>
            new ChunkGroupPiece("bad", [(0, 0, Side.S)], [new CylinderBlocker(75, 10, 2)], [], ["forest"]).Members().ToList());

    [Fact]
    public void Refuse_a_single_piece_blocker_outside_its_cell()
    {
        var piece = Sample with { Name = "bad", Blockers = [new BoxBlocker(25, 32, 5, 10)] };

        var refusal = Assert.Throws<InvalidOperationException>(() => ChunkFiles.For([piece]));
        Assert.Contains("bad", refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(31f, 5f)]
    [InlineData(5f, -1f)]
    [InlineData(float.NaN, 5f)]
    public void Refuse_a_single_piece_slot_outside_its_cell(float x, float z)
    {
        var piece = Sample with { Name = "bad", Slots = [new Slot("pack", x, z)] };

        Assert.Throws<InvalidOperationException>(() => ChunkFiles.For([piece]));
    }

    [Fact]
    public void Accept_a_single_piece_blocker_and_slot_on_its_cell_edges() =>
        Assert.Single(ChunkFiles.For([Sample with { Blockers = [new BoxBlocker(0, 4, 26, 30)], Slots = [new Slot("pack", 30, 0)] }]));

    // ---- town squares (town beautification, 2026-10-01) ----

    private static TownSquare Square(params TownPiece[] pieces) => new("town_test_01", 0, 0, [Side.N],
        [new WallSegment("Wall_S", 0, 30, -0.25f, 0.25f), new WallSegment("Wall_N_L", 0, 12, 29.75f, 30.25f), new WallSegment("Wall_N_R", 18, 30, 29.75f, 30.25f)],
        pieces, IsEntry: false, HasForwardPortal: false, ["town"]);

    [Fact]
    public void Accept_a_square_whose_pieces_keep_the_owners_rules() =>
        TownRules.Check(Square(
            new BoxPiece("House", "body", Material.Plaster, 4, 10, 4, 10, 0, 3.2f),
            new GablePiece("House", "roof", Material.Roof, 3.7f, 10.3f, 3.7f, 10.3f, 3.2f, 5f),
            new CylinderPiece("Well", "ring", Material.Stone, 20, 20, 1, 0, 1),
            new BoxPiece("House", "porch deck", Material.Wood, 10, 12, 5, 9, 0, 0.2f, Walkable: true),
            new BoxPiece("House", "porch roof", Material.Roof, 9.9f, 12.4f, 4.7f, 9.3f, 3.2f, 3.5f)));

    [Fact]
    public void Refuse_a_piece_within_two_metres_of_a_wall()
    {
        // A crate 0.75 m from Wall_S (Z -0.25..0.25).
        var refusal = Assert.Throws<InvalidOperationException>(() =>
            TownRules.Check(Square(new BoxPiece("Crate", "crate", Material.Wood, 10, 11, 1, 2, 0, 1))));
        Assert.Contains("Wall_S", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuse_a_piece_in_a_doorway_lane() =>
        Assert.Contains("lane", Assert.Throws<InvalidOperationException>(() =>
            TownRules.Check(Square(new BoxPiece("Crate", "crate", Material.Wood, 14, 15, 27.5f, 28.5f, 0, 1)))).Message, StringComparison.Ordinal);

    [Fact]
    public void Refuse_a_roof_with_less_than_two_and_a_half_metres_of_headroom() =>
        Assert.Contains("headroom", Assert.Throws<InvalidOperationException>(() =>
            TownRules.Check(Square(new BoxPiece("Stall", "roof", Material.Cloth, 10, 13, 10, 13, 2.4f, 2.65f)))).Message, StringComparison.Ordinal);

    [Fact]
    public void Refuse_a_walkable_riser_over_the_navmesh_step() =>
        Assert.Contains("riser", Assert.Throws<InvalidOperationException>(() =>
            TownRules.Check(Square(new BoxPiece("Bank", "step", Material.Stone, 10, 12, 10, 14, 0, 0.35f, Walkable: true)))).Message, StringComparison.Ordinal);

    [Fact]
    public void Refuse_a_solid_in_the_arrival_to_portal_corridor()
    {
        var sw = new TownSquare("town_sw_01", 0, 0, [Side.N], [], [new BoxPiece("Crate", "crate", Material.Wood, 14, 15, 20, 21, 0, 1)], true, false, ["town"]);
        Assert.Contains("corridor", Assert.Throws<InvalidOperationException>(() => TownRules.Check(sw)).Message, StringComparison.Ordinal);

        var lintel = new TownSquare("town_nw_01", 0, 1, [Side.S], [], [new BoxPiece("Gate arch", "lintel", Material.Stone, 10.5f, 19.5f, 11.25f, 12.75f, 4.5f, 5.5f)], false, true, ["town"]);
        TownRules.Check(lintel);   // above 2.5 m: allowed
    }

    [Theory]
    [InlineData(25f, 32f, 5f, 10f)]
    [InlineData(5f, 10f, -0.5f, 3f)]
    [InlineData(float.NaN, 10f, 5f, 10f)]
    public void Refuse_a_piece_outside_its_cell(float minX, float maxX, float minZ, float maxZ) =>
        Assert.Throws<InvalidOperationException>(() =>
            Square(new BoxPiece("Crate", "crate", Material.Wood, minX, maxX, minZ, maxZ, 0, 1)).Validate());

    [Fact]
    public void Refuse_a_piece_named_like_a_wall() =>
        Assert.Throws<InvalidOperationException>(() =>
            Square(new BoxPiece("Wall", "post", Material.Stone, 10, 11, 10, 11, 0, 3)).Validate());

    [Theory]
    [InlineData("Town hall", "porch deck", "TownHall_porch_deck")]
    [InlineData("Hunter's lodge", "post", "HuntersLodge_post")]
    [InlineData("Crates (lodge)", "crate 3 (stacked)", "CratesLodge_crate_3_stacked")]
    [InlineData("General-goods stall", "awning", "GeneralGoodsStall_awning")]
    [InlineData("Benches", "bench NE", "Benches_bench_ne")]
    public void Name_an_object_from_its_building_and_part(string building, string part, string expected) =>
        Assert.Equal(expected, TownSquare.ObjectName(building, part));

    private static readonly TownSquare SampleSquare = new("town_test_01", 0, 0, [Side.N, Side.E],
        [
            new WallSegment("Wall_N_L", 0, 12, 29.75f, 30.25f), new WallSegment("Wall_N_R", 18, 30, 29.75f, 30.25f),
            new WallSegment("Wall_E_L", 29.75f, 30.25f, 0, 12), new WallSegment("Wall_E_R", 29.75f, 30.25f, 18, 30),
            new WallSegment("Wall_S", 0, 30, -0.25f, 0.25f), new WallSegment("Wall_W", -0.25f, 0.25f, 0, 30),
        ],
        [
            new BoxPiece("House", "body", Material.Plaster, 4, 10, 4, 10, 0, 3.2f),
            new GablePiece("House", "roof", Material.Roof, 3.7f, 10.3f, 3.7f, 12.3f, 3.2f, 5f),
            new CylinderPiece("House", "post", Material.Wood, 11, 5, 0.12f, 0, 3),
            new CylinderPiece("House", "post", Material.Wood, 11, 9, 0.12f, 0, 3),
            new BoxPiece("House", "porch deck", Material.Wood, 10, 12, 4.5f, 9.5f, 0, 0.2f, Walkable: true),
        ], IsEntry: true, HasForwardPortal: false, ["town", "entry"]);

    [Fact]
    public void Write_a_town_square_as_named_objects_with_materials()
    {
        string obj = ObjWriter.Write(SampleSquare);
        string[] lines = obj.Split('\n');

        Assert.Equal("# Avalon chunk: town_test_01", lines[0]);
        List<string> objects = lines.Where(l => l.StartsWith("o ", StringComparison.Ordinal)).Select(l => l[2..]).ToList();
        Assert.Equal(["Floor", "Wall_N_L", "Wall_N_R", "Wall_E_L", "Wall_E_R", "Wall_S", "Wall_W",
            "House_body", "House_roof", "House_post_1", "House_post_2", "House_porch_deck"], objects);

        List<string> materials = lines.Where(l => l.StartsWith("usemtl ", StringComparison.Ordinal)).Select(l => l[7..]).ToList();
        Assert.Equal(["stone", "stone", "stone", "stone", "stone", "stone", "plaster", "roof", "wood", "wood", "wood"], materials);
        Assert.DoesNotContain(lines, l => l.StartsWith("mtllib", StringComparison.Ordinal));

        // Every usemtl follows its "o" line directly, before any vertex.
        for (int i = 1; i < lines.Length; i++)
            if (lines[i].StartsWith("usemtl ", StringComparison.Ordinal))
                Assert.StartsWith("o ", lines[i - 1], StringComparison.Ordinal);
    }

    /// <summary>The town's floor and walls, by name (ReadTown in TownNpcPlacementShould reads them by name).</summary>
    [Fact]
    public void Reproduce_the_floor_and_wall_boxes_of_a_town_square()
    {
        Dictionary<string, (float MinX, float MaxX, float MinY, float MaxY, float MinZ, float MaxZ)> boxes = BoundsByObject(ObjWriter.Write(SampleSquare));

        Assert.Equal((0f, 30f, -0.05f, 0.05f, 0f, 30f), boxes["Floor"]);
        Assert.Equal((0f, 12f, 0f, 2f, 29.75f, 30.25f), boxes["Wall_N_L"]);
        Assert.Equal((29.75f, 30.25f, 0f, 2f, 18f, 30f), boxes["Wall_E_R"]);
        Assert.Equal((0f, 30f, 0f, 2f, -0.25f, 0.25f), boxes["Wall_S"]);
        Assert.Equal((-0.25f, 0.25f, 0f, 2f, 0f, 30f), boxes["Wall_W"]);
    }

    [Fact]
    public void Draw_a_gable_with_its_ridge_along_the_longer_axis()
    {
        List<Vector3> vertices = Vertices(ObjWriter.Write(SampleSquare))["House_roof"];

        Assert.Equal(6, vertices.Count);
        List<Vector3> ridge = vertices.Where(v => v.y == 5f).ToList();
        Assert.Equal(2, ridge.Count);
        Assert.All(ridge, v => Assert.Equal(7f, v.x));          // the footprint is 6.6 x 8.6: the ridge runs along Z, at the X middle
        Assert.Equal([3.7f, 12.3f], ridge.Select(v => v.z).Order());
        Assert.Equal(4, vertices.Count(v => v.y == 3.2f));
    }

    [Fact]
    public void Wind_every_face_of_a_town_square_outward() => AssertOutward(ObjWriter.Write(SampleSquare));

    private static readonly TownSquare RingSquare = SampleSquare with
    {
        Pieces = [new RingPiece("Fountain", "basin", Material.Stone, 15, 15, 2.2f, 1.8f, 0, 1.1f), new CylinderPiece("Fountain", "water", Material.Water, 15, 15, 1.78f, 0.9f, 1f)],
    };

    /// <summary>
    /// A ring is not star-shaped, so the generic centroid test cannot judge it: every face must point away from its own
    /// segment's middle, found from the geometry alone (the ring's centre is the object's centroid, the rim's middle
    /// radius the vertices' mean distance from it).
    /// </summary>
    [Fact]
    public void Wind_every_face_of_a_ring_outward()
    {
        string obj = ObjWriter.Write(RingSquare);
        var vertices = new List<Vector3>();
        var ringVertices = new List<Vector3>();
        var faces = new List<(int A, int B, int C)>();
        string current = "";
        foreach (string line in obj.Split('\n'))
        {
            string[] p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (line.StartsWith("o ", StringComparison.Ordinal)) current = p[1];
            else if (line.StartsWith("v ", StringComparison.Ordinal))
            {
                var v = new Vector3(float.Parse(p[1], CultureInfo.InvariantCulture), float.Parse(p[2], CultureInfo.InvariantCulture), float.Parse(p[3], CultureInfo.InvariantCulture));
                vertices.Add(v);
                if (current == "Fountain_basin") ringVertices.Add(v);
            }
            else if (line.StartsWith("f ", StringComparison.Ordinal) && current == "Fountain_basin")
                faces.Add((int.Parse(p[1], CultureInfo.InvariantCulture) - 1, int.Parse(p[2], CultureInfo.InvariantCulture) - 1, int.Parse(p[3], CultureInfo.InvariantCulture) - 1));
        }

        Assert.Equal(48, ringVertices.Count);
        Assert.Equal(96, faces.Count);
        var centre = new Vector3(ringVertices.Average(v => v.x), ringVertices.Average(v => v.y), ringVertices.Average(v => v.z));
        float midRadius = ringVertices.Average(v => MathF.Sqrt((v.x - centre.x) * (v.x - centre.x) + (v.z - centre.z) * (v.z - centre.z)));
        foreach ((int a, int b, int c) in faces)
        {
            Vector3 fc = (vertices[a] + vertices[b] + vertices[c]) / 3f;
            float dx = fc.x - centre.x, dz = fc.z - centre.z, d = MathF.Sqrt(dx * dx + dz * dz);
            var inside = new Vector3(centre.x + dx / d * midRadius, centre.y, centre.z + dz / d * midRadius);
            Vector3 u = vertices[b] - vertices[a], w = vertices[c] - vertices[a];
            var normal = new Vector3(u.y * w.z - u.z * w.y, u.z * w.x - u.x * w.z, u.x * w.y - u.y * w.x);
            Vector3 outward = fc - inside;
            Assert.True(normal.x * outward.x + normal.y * outward.y + normal.z * outward.z > 0, $"a face of the ring at {fc} faces inward");
        }
    }

    /// <summary>The rim blocks a walk, and the water inside is no ground a walk from outside reaches.</summary>
    [Fact]
    public void Bake_a_ring_a_walk_cannot_enter()
    {
        MapNavigator navigator = GeneratedChunkBake.Bake([(ObjWriter.Write(RingSquare), 0, 0)]);

        Vector3 stop = navigator.RaycastWalkable(new Vector3(5f, 0.15f, 15f), new Vector3(15f, 0.15f, 15f));
        Assert.True(stop.x < 12.85f, $"the walk entered the rim to x = {stop.x}");
        List<Vector3> path = navigator.FindPath(new Vector3(5f, 1f, 15f), new Vector3(15f, 1.3f, 15f));
        Assert.True(path.Count == 0 || MathF.Sqrt((path[^1].x - 15f) * (path[^1].x - 15f) + (path[^1].z - 15f) * (path[^1].z - 15f)) > 1.8f,
            $"a path reached the water at {(path.Count > 0 ? path[^1] : Vector3.zero)}");
    }

    [Fact]
    public void Refuse_a_ring_with_no_rim() =>
        Assert.Throws<InvalidOperationException>(() =>
            Square(new RingPiece("Well", "ring", Material.Stone, 15, 15, 1f, 1f, 0, 1.05f)).Validate());

    [Fact]
    public void Never_write_negative_zero_for_a_town_square()
    {
        TownSquare square = SampleSquare with { Pieces = [new BoxPiece("Crate", "crate", Material.Wood, 0.00001f, 1, 10, 11, -0.00001f, 1)] };

        string obj = ObjWriter.Write(square);

        Assert.DoesNotContain("-0 ", obj, StringComparison.Ordinal);
        Assert.DoesNotContain("-0\n", obj, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", obj, StringComparison.Ordinal);
    }

    [Fact]
    public void Bake_a_town_square_a_player_can_walk_onto_its_porch()
    {
        MapNavigator navigator = GeneratedChunkBake.Bake([(ObjWriter.Write(SampleSquare), 0, 0)]);

        Assert.Equal(NavmeshGroundKind.Under, navigator.FindGround(new Vector3(11f, 1f, 7f), out Vector3 deck));   // on the porch deck
        Assert.InRange(deck.y, -0.1f, 0.5f);
        Vector3 stop = navigator.RaycastWalkable(new Vector3(20f, 0.15f, 7f), new Vector3(2f, 0.15f, 7f));
        Assert.True(stop.x >= 10f && stop.x < 11f, $"the walk west along z = 7 stopped at x = {stop.x}, expected on the deck against the body at x = 10");
    }

    [Fact]
    public async Task Read_a_town_square_back_through_the_seeders_catalog_reader()
    {
        string root = Path.Combine(Path.GetTempPath(), $"avalon-chunkgen-{Guid.NewGuid():N}");
        try
        {
            CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Maps"), root);
            foreach ((string name, string obj, string json) in ChunkFiles.For([SampleSquare]))
            {
                File.WriteAllText(Path.Combine(root, "Chunks", name + ".obj"), obj);
                File.WriteAllText(Path.Combine(root, "Chunks", name + ".json"), json);
            }

            ChunkCatalogFiles files = await ChunkCatalogSeeder.ReadCatalogAsync(root);

            ChunkMetaDto read = Assert.Single(files.Chunks, c => c.Name == "town_test_01");
            Assert.Equal(new SpawnSlotDto("entry", 15, 0, 15), Assert.Single(read.SpawnSlots));
            Assert.Equal(["town", "entry"], read.Tags);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Vertices by "o" object, in file order; "usemtl" and other lines are ignored, as the bake ignores them.</summary>
    private static Dictionary<string, List<Vector3>> Vertices(string obj)
    {
        var objects = new Dictionary<string, List<Vector3>>(StringComparer.Ordinal);
        string current = "";
        foreach (string line in obj.Split('\n'))
        {
            string[] p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (line.StartsWith("o ", StringComparison.Ordinal)) { current = p[1]; objects[current] = []; }
            else if (line.StartsWith("v ", StringComparison.Ordinal))
                objects[current].Add(new Vector3(float.Parse(p[1], CultureInfo.InvariantCulture), float.Parse(p[2], CultureInfo.InvariantCulture),
                    float.Parse(p[3], CultureInfo.InvariantCulture)));
        }
        return objects;
    }

    private static Dictionary<string, (float MinX, float MaxX, float MinY, float MaxY, float MinZ, float MaxZ)> BoundsByObject(string obj) =>
        Vertices(obj).ToDictionary(o => o.Key, o => (o.Value.Min(v => v.x), o.Value.Max(v => v.x), o.Value.Min(v => v.y), o.Value.Max(v => v.y),
            o.Value.Min(v => v.z), o.Value.Max(v => v.z)), StringComparer.Ordinal);

    private static void AssertOutward(string obj)
    {
        var vertices = new List<Vector3>();
        string current = "";
        var objects = new Dictionary<string, List<Vector3>>(StringComparer.Ordinal);
        var faces = new List<(string Object, int A, int B, int C)>();
        foreach (string line in obj.Split('\n'))
        {
            string[] p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (line.StartsWith("o ", StringComparison.Ordinal)) { current = p[1]; objects[current] = []; }
            else if (line.StartsWith("v ", StringComparison.Ordinal))
            {
                var v = new Vector3(float.Parse(p[1], CultureInfo.InvariantCulture), float.Parse(p[2], CultureInfo.InvariantCulture),
                    float.Parse(p[3], CultureInfo.InvariantCulture));
                vertices.Add(v);
                objects[current].Add(v);
            }
            else if (line.StartsWith("f ", StringComparison.Ordinal))
                faces.Add((current, int.Parse(p[1], CultureInfo.InvariantCulture) - 1, int.Parse(p[2], CultureInfo.InvariantCulture) - 1,
                    int.Parse(p[3], CultureInfo.InvariantCulture) - 1));
        }

        Assert.NotEmpty(faces);
        foreach ((string name, int a, int b, int c) in faces)
        {
            List<Vector3> solid = objects[name];
            var centre = new Vector3(solid.Average(v => v.x), solid.Average(v => v.y), solid.Average(v => v.z));
            Vector3 u = vertices[b] - vertices[a], w = vertices[c] - vertices[a];
            var normal = new Vector3(u.y * w.z - u.z * w.y, u.z * w.x - u.x * w.z, u.x * w.y - u.y * w.x);
            Vector3 outward = (vertices[a] + vertices[b] + vertices[c]) / 3f - centre;
            Assert.True(normal.x * outward.x + normal.y * outward.y + normal.z * outward.z > 0, $"a face of {name} faces inward");
        }
    }

    [Fact]
    public void Describe_a_square_as_the_seeders_catalog_entry()
    {
        var sw = new TownSquare("town_sw_01", 0, 0, [Side.N, Side.E], [], [], true, false, ["town", "entry"]);
        var nw = new TownSquare("town_nw_01", 0, 1, [Side.E, Side.S], [], [], false, true, ["town"]);

        ChunkMetaDto swMeta = sw.ToMeta();
        ChunkMetaDto nwMeta = nw.ToMeta();

        Assert.Equal(new SpawnSlotDto("entry", 15, 0, 15), Assert.Single(swMeta.SpawnSlots));
        Assert.Empty(swMeta.PortalSlots);
        Assert.Equal(["center"], swMeta.Exits["N"]);
        Assert.Empty(swMeta.Exits["S"]);
        Assert.Equal(new PortalSlotDto("Forward", 15, 0, 15), Assert.Single(nwMeta.PortalSlots));
        Assert.Empty(nwMeta.SpawnSlots);
    }
}
