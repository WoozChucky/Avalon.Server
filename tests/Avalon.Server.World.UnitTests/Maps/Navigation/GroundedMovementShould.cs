using Avalon.Common.Mathematics;
using Avalon.World.Maps.Navigation;
using Avalon.World.Testing.Scenarios;
using DotRecast.Detour;
using DotRecast.Recast.Geom;
using DotRecast.Recast.Toolset.Builder;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Server.World.UnitTests.Maps.Navigation;

public class GroundedMovementShould
{
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
        Assert.Equal(LegacyMove(nav, from, to), actual);
    }

    [Fact]
    public void Match_existing_movement_across_town_walls_edges_and_relocations()
    {
        var nav = new MapNavigator(NullLoggerFactory.Instance);
        nav.LoadFromNavMesh(TownNavmesh.Shared);
        var random = new Random(896);
        int moved = 0, clamped = 0, snapped = 0;
        for (int i = 0; i < 3000; i++)
        {
            // Fresh positions, not a carried polygon hint: includes relocation and off-mesh starts.
            var from = new Vector3((float)random.NextDouble() * 64 - 2, i % 3 * 0.5f,
                (float)random.NextDouble() * 64 - 2);
            float reach = i % 3 == 0 ? 0 : i % 3 == 1 ? 0.1f : 4f;
            var to = new Vector3(from.x + reach, from.y, from.z - reach);
            Vector3 expected = LegacyMove(nav, from, to);
            Vector3 actual = nav.MoveOnGround(from, to);
            Assert.Equal(expected, actual);
            if (actual.x != from.x || actual.z != from.z) moved++;
            if (actual.x != to.x || actual.z != to.z) clamped++;
            if (actual.y != from.y) snapped++;
        }

        Assert.True(moved > 100 && clamped > 100 && snapped > 100);
        // Replacing the mesh or having none must not reuse a previous call's polygon/floor.
        nav.LoadFromNavMesh(s_floors.Value);
        Assert.Equal(LegacyMove(nav, new Vector3(0, 3.5f, 0), new Vector3(0, 3.5f, 0)),
            nav.MoveOnGround(new Vector3(0, 3.5f, 0), new Vector3(0, 3.5f, 0)));
        nav = new MapNavigator(NullLoggerFactory.Instance);
        Assert.Equal(new Vector3(3, 7, 5), nav.MoveOnGround(new Vector3(1, 7, 2), new Vector3(3, 9, 5)));
    }

    private static Vector3 LegacyMove(MapNavigator nav, Vector3 from, Vector3 to)
    {
        Vector3 clamped = nav.RaycastWalkable(from, to);
        return new Vector3(clamped.x, nav.SampleGroundHeight(clamped.x, from.y, clamped.z), clamped.z);
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
