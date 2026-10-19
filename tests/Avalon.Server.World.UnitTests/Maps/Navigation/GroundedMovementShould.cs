using Avalon.Common.Mathematics;
using Avalon.World.Maps.Navigation;
using Avalon.World.Testing.Navigation;
using Avalon.World.Testing.Scenarios;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Recast.Geom;
using DotRecast.Recast.Toolset.Builder;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Server.World.UnitTests.Maps.Navigation;

public class GroundedMovementShould
{
    private const int ForestSeed = 7;
    private static readonly Lazy<DtNavMesh> s_floors = new(BakeFloors);

    // A ground snap must still happen for an idle input, on either floor and on a slope.
    [Theory]
    [InlineData(0f, 0.5f, 0f, 0f, 0f)]
    [InlineData(0f, 3.5f, 0f, 0f, 3f)]
    [InlineData(40f, 2.5f, 0f, 0f, 2f)]
    [InlineData(40f, 2.5f, 0.1f, 0f, 2.02f)]
    [InlineData(100f, 8f, 0.1f, 0f, 8f)]
    public void Preserve_ground_snapping_floor_selection_and_failed_lookup(float x, float y,
        float dx, float dz, float ground)
    {
        var nav = new MapNavigator(NullLoggerFactory.Instance);
        nav.LoadFromNavMesh(s_floors.Value);
        var from = new Vector3(x, y, 0);
        var to = new Vector3(x + dx, y, dz);

        Vector3 actual = nav.MoveOnGround(from, to);

        // Recast voxelizes heights in 0.2 m increments; this also catches selecting the wrong storey.
        Assert.InRange(actual.y, ground - 0.01f, ground + 0.25f);
        Assert.Equal(new DetourNavigationReference(s_floors.Value).MoveOnGround(from, to), actual);
    }

    /// <summary>
    /// The navigator's searches (#978) answer what DotRecast's full nearest-polygon searches answer, to the bit, on the
    /// town, a forest and two overlapping floors (the town and the forest have overlapping floors too: 60 of the town's
    /// 313 polygons, and 118 of the forest's 603 before #1109, have a second floor over or under their centre): the
    /// ground, a player's step, the walkable raycast (a projectile's step), a route and a line of sight, from every
    /// polygon's corners, edges and inside, at the polygon's height, at exactly the climb height above and below it,
    /// just past it, and between floors; idle, a step, a long move into walls, a step that ends on a corner, and (route
    /// and sight, from one point in sixteen on the larger maps) to the previous point. Corners and edges are where two
    /// polygons score the same and the first in DotRecast's order wins; just past the climb, and beside the mesh, the
    /// full search decides.
    /// </summary>
    [Theory]
    [InlineData("town")]
    [InlineData("forest")]
    [InlineData("floors")]
    public void Answer_as_the_full_searches_do(string map)
    {
        DtNavMesh mesh = map switch
        {
            "town" => TownNavmesh.Shared,
            "forest" => ForestLayouts.For(ForestSeed).NavMesh,
            _ => s_floors.Value,
        };
        var nav = new MapNavigator(NullLoggerFactory.Instance);
        nav.LoadFromNavMesh(mesh);
        var reference = new DetourNavigationReference(mesh);
        var random = new Random(978);
        int standing = 0, beside = 0, moved = 0, clamped = 0, routed = 0, seen = 0, hidden = 0;
        var path = new List<Vector3>();
        var expectedPath = new List<Vector3>();
        Vector3 previous = default;
        int queried = 0;
        int stride = map == "floors" ? 1 : 16;

        foreach ((Vector3 point, Vector3 corner) in Points(mesh, random))
        {
            NavmeshGroundKind expectedKind = reference.FindGround(point, out Vector3 expectedGround);
            Assert.Equal(expectedKind, nav.FindGround(point, out Vector3 ground));
            Assert.Equal(expectedGround, ground);
            if (reference.StandsOnPolygon(point))
                standing++;
            else
                beside++;

            float angle = (float)(random.NextDouble() * Math.Tau);
            var direction = new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle));
            ReadOnlySpan<Vector3> targets =
            [
                point,
                point + direction * (4f / 60f),
                point + direction * 4f,
                new Vector3(corner.x, point.y, corner.z),
            ];
            foreach (Vector3 to in targets)
            {
                Vector3 expected = reference.MoveOnGround(point, to);
                Assert.Equal(expected, nav.MoveOnGround(point, to));
                Assert.Equal(reference.RaycastWalkable(point, to), nav.RaycastWalkable(point, to));
                if (expected.x != point.x || expected.z != point.z)
                    moved++;
                if (expected.x != to.x || expected.z != to.z)
                    clamped++;
            }

            // Routes and sights from one point in sixteen on the town and the forest, where a route costs as much as all
            // the rest together; from every point on the small two-floor mesh.
            foreach (Vector3 to in queried++ % stride == 0 ? [targets[2], targets[3], previous] : ReadOnlySpan<Vector3>.Empty)
            {
                reference.FindPath(point, to, expectedPath);
                nav.FindPath(point, to, path);
                Assert.Equal(expectedPath, path);
                routed += path.Count > 0 ? 1 : 0;

                bool visible = reference.HasVisibility(point, to);
                Assert.Equal(visible, nav.HasVisibility(point, to));
                seen += visible ? 1 : 0;
                hidden += visible ? 0 : 1;
            }

            previous = point;
        }

        // Both of the search's paths, walls, routes and both answers of a line of sight were taken. The counts are the
        // same on every run and platform (the points are visited in grid order, not the bake's tile order); the floors
        // are about half the smallest map's count (the two-floor mesh's: standing 1,069, beside 1,601, moved 6,934,
        // clamped 1,389, hidden 1,009; the town's routed 3,636 and seen 2,660), so they hold only that each path is
        // exercised.
        Assert.True(standing > 500 && beside > 800 && moved > 3000 && clamped > 600 && routed > 1500 && seen > 1300
            && hidden > 500,
            $"standing {standing}, beside {beside}, moved {moved}, clamped {clamped}, routed {routed}, seen {seen}, hidden {hidden}");
    }

    /// <summary>
    /// Two polygons stacked 0.79 m apart, under the 0.8 m climb, so a point on either scores zero on both and
    /// DotRecast answers whichever comes first in its order: for one of the two points that is the polygon 0.79 m
    /// away. The narrow box must reach that far up and down. Baked meshes cannot hold this (floors stand at least the
    /// agent's 2 m apart, and their bounds are rounded out to 0.3 m); this one is built by hand with 1 cm cells, so
    /// a narrow box lower than the climb misses the far polygon and answers the near one.
    /// </summary>
    [Fact]
    public void Reach_every_polygon_within_the_climb_up_and_down()
    {
        DtNavMesh mesh = StackedPolygons(gapCells: 79, climb: 0.8f);
        var nav = new MapNavigator(NullLoggerFactory.Instance);
        nav.LoadFromNavMesh(mesh);
        var reference = new DetourNavigationReference(mesh);
        int farther = 0;

        foreach (float y in (ReadOnlySpan<float>)[0f, 0.79f])
        {
            var point = new Vector3(0.5f, y, 0.5f);
            Assert.Equal(NavmeshGroundKind.Under, reference.FindGround(point, out Vector3 expected));
            Assert.Equal(NavmeshGroundKind.Under, nav.FindGround(point, out Vector3 ground));
            Assert.Equal(expected, ground);
            if (MathF.Abs(expected.y - y) > 0.5f)
                farther++;
        }

        // The case is only a test if DotRecast's answer is the far polygon for one of the points.
        Assert.Equal(1, farther);
    }

    /// <summary>
    /// A 1 m square at 0 m whose detail surface rises to 0.8 m in the middle, under a second square at 1.8 m, with a
    /// 0.9 m climb: from 1.3 m up, the raised surface (0.72 m under the point) and the upper square both score zero, and
    /// DotRecast answers the first. A tile without a bounding-volume tree is searched by its polygons' corners, which
    /// leave the lower square out of a narrow box that its raised surface reaches; so such a mesh takes the full box.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Answer_as_the_full_search_does_with_or_without_a_tree(bool tree)
    {
        DtNavMesh mesh = RaisedDetailUnderASquare(tree);
        var nav = new MapNavigator(NullLoggerFactory.Instance);
        nav.LoadFromNavMesh(mesh);
        var point = new Vector3(0.45f, 1.3f, 0.45f);
        NavmeshGroundKind expectedKind = new DetourNavigationReference(mesh).FindGround(point, out Vector3 expected);

        Assert.Equal(expectedKind, nav.FindGround(point, out Vector3 ground));
        Assert.Equal(expected, ground);
    }

    [Fact]
    public void Answer_for_the_mesh_loaded_now()
    {
        var nav = new MapNavigator(NullLoggerFactory.Instance);
        nav.LoadFromNavMesh(TownNavmesh.Shared);
        nav.MoveOnGround(new Vector3(15, 0, 15), new Vector3(15.1f, 0, 15));

        // Replacing the mesh or having none must not reuse a previous call's polygon or floor.
        nav.LoadFromNavMesh(s_floors.Value);
        Assert.Equal(new DetourNavigationReference(s_floors.Value).MoveOnGround(new Vector3(0, 3.5f, 0), new Vector3(0, 3.5f, 0)),
            nav.MoveOnGround(new Vector3(0, 3.5f, 0), new Vector3(0, 3.5f, 0)));
        nav = new MapNavigator(NullLoggerFactory.Instance);
        Assert.Equal(new Vector3(3, 7, 5), nav.MoveOnGround(new Vector3(1, 7, 2), new Vector3(3, 9, 5)));
    }

    /// <summary>
    /// Points on and around every polygon of the mesh, each with a corner of its polygon to step onto: the corners, the
    /// edges' midpoints and a point along each, a point inside; at the polygon's height there, exactly the tile's climb
    /// above and below it, a little past the climb, and 1.5 m up (between the floors of <see cref="BakeFloors" />).
    /// Tiles are visited by their place in the grid, not their slot: the bake adds tiles in the order its worker
    /// threads finish them, which differs from bake to bake, and the random draws and the one-in-sixteen routes must
    /// fall on the same points every run.
    /// </summary>
    private static IEnumerable<(Vector3 Point, Vector3 Corner)> Points(DtNavMesh mesh, Random random)
    {
        var tiles = new List<DtMeshTile>();
        for (int t = 0; t < mesh.GetMaxTiles(); t++)
        {
            DtMeshTile? slot = mesh.GetTile(t);
            if (slot?.data?.header is not null)
                tiles.Add(slot);
        }

        foreach (DtMeshTile tile in tiles.OrderBy(t => t.data.header.y).ThenBy(t => t.data.header.x)
                     .ThenBy(t => t.data.header.layer))
        {

            float climb = tile.data.header.walkableClimb;
            for (int p = 0; p < tile.data.header.polyCount; p++)
            {
                DtPoly poly = tile.data.polys[p];
                if (poly.GetPolyType() == DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION)
                    continue;

                int n = poly.vertCount;
                var on = new List<Vector3>(2 * n + 1);
                Vector3 centre = default;
                for (int i = 0; i < n; i++)
                {
                    Vector3 a = Vertex(tile, poly, i), b = Vertex(tile, poly, (i + 1) % n);
                    float along = (float)random.NextDouble();
                    on.Add(a);
                    on.Add(Lerp(a, b, 0.5f));
                    on.Add(Lerp(a, b, along));
                    centre += a / n;
                }

                on.Add(Lerp(centre, Vertex(tile, poly, random.Next(n)), (float)random.NextDouble()));
                foreach (Vector3 point in on)
                {
                    Vector3 corner = Vertex(tile, poly, random.Next(n));
                    foreach (float up in new[] { 0, climb, -climb, climb + 0.05f, 1.5f })
                        yield return (new Vector3(point.x, point.y + up, point.z), corner);
                }
            }
        }
    }

    private static Vector3 Vertex(DtMeshTile tile, DtPoly poly, int i)
    {
        int v = poly.verts[i] * 3;
        return new Vector3(tile.data.verts[v], tile.data.verts[v + 1], tile.data.verts[v + 2]);
    }

    private static Vector3 Lerp(Vector3 a, Vector3 b, float t) =>
        new(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);

    /// <summary>One tile of two 1 m squares, one <paramref name="gapCells" /> centimetres above the other, unconnected.</summary>
    private static DtNavMesh StackedPolygons(int gapCells, float climb)
    {
        const int Nvp = 6, None = 0xffff;
        int[] Square(int first) => [first, first + 1, first + 2, first + 3, None, None, None, None, None, None, None, None];
        var create = new DtNavMeshCreateParams
        {
            verts = [0, 0, 0, 0, 0, 100, 100, 0, 100, 100, 0, 0, 0, gapCells, 0, 0, gapCells, 100, 100, gapCells, 100, 100, gapCells, 0],
            vertCount = 8,
            polys = [.. Square(0), .. Square(4)],
            polyFlags = [1, 1],
            polyAreas = [0, 0],
            polyCount = 2,
            nvp = Nvp,
            cs = 0.01f,
            ch = 0.01f,
            walkableHeight = 2f,
            walkableRadius = 0.6f,
            walkableClimb = climb,
            bmin = new RcVec3f(0, 0, 0),
            bmax = new RcVec3f(1, 2, 1),
            buildBvTree = true,
        };
        var mesh = new DtNavMesh();
        mesh.Init(DtNavMeshBuilder.CreateNavMeshData(create), Nvp, 0);
        return mesh;
    }

    /// <summary>
    /// One tile, 1 cm cells: a 1 m square at 0 m with a detail vertex 0.8 m up in its middle, and an unconnected 1 m
    /// square at 1.8 m over it; climb 0.9 m; with or without a bounding-volume tree.
    /// </summary>
    private static DtNavMesh RaisedDetailUnderASquare(bool tree)
    {
        const int Nvp = 6, None = 0xffff;
        int[] Square(int first) => [first, first + 1, first + 2, first + 3, None, None, None, None, None, None, None, None];
        var create = new DtNavMeshCreateParams
        {
            verts = [0, 0, 0, 0, 0, 100, 100, 0, 100, 100, 0, 0, 0, 180, 0, 0, 180, 100, 100, 180, 100, 100, 180, 0],
            vertCount = 8,
            polys = [.. Square(0), .. Square(4)],
            polyFlags = [1, 1],
            polyAreas = [0, 0],
            polyCount = 2,
            nvp = Nvp,
            detailMeshes = [0, 5, 0, 4, 5, 4, 4, 2],
            detailVerts =
            [
                0, 0, 0, 0, 0, 1, 1, 0, 1, 1, 0, 0, 0.5f, 0.8f, 0.5f,
                0, 1.8f, 0, 0, 1.8f, 1, 1, 1.8f, 1, 1, 1.8f, 0,
            ],
            detailVertsCount = 9,
            detailTris = [0, 1, 4, 0, 1, 2, 4, 0, 2, 3, 4, 0, 3, 0, 4, 0, 0, 1, 2, 0, 0, 2, 3, 0],
            detailTriCount = 6,
            cs = 0.01f,
            ch = 0.01f,
            walkableHeight = 2f,
            walkableRadius = 0.6f,
            walkableClimb = 0.9f,
            bmin = new RcVec3f(0, 0, 0),
            bmax = new RcVec3f(1, 2, 1),
            buildBvTree = tree,
        };
        var mesh = new DtNavMesh();
        mesh.Init(DtNavMeshBuilder.CreateNavMeshData(create), Nvp, 0);
        return mesh;
    }

    private static DtNavMesh BakeFloors()
    {
        float[] vertices =
        [
            -20, 0, -20, -20, 0, 20, 20, 0, 20, 20, 0, -20,
            -10, 3, -10, -10, 3, 10, 10, 3, 10, 10, 3, -10,
            30, 0, -10, 30, 0, 10, 50, 4, 10, 50, 4, -10,
        ];
        int[] faces = [0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7, 8, 9, 10, 8, 10, 11];
        return new TileNavMeshBuilder().Build(new RcSampleInputGeomProvider(vertices, faces),
            NavmeshBuildSettings.Create()).NavMesh;
    }
}
