using System.Globalization;
using Avalon.ChunkGen;
using Avalon.Common.Mathematics;
using Avalon.Database.World.Seeding;
using Avalon.World.Maps.Navigation;
using Xunit;

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

    private static void CopyDirectory(string source, string target)
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
}
