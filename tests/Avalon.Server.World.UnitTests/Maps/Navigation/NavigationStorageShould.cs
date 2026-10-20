using Avalon.World.Maps.Navigation;
using Avalon.World.Testing.Navigation;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using Xunit.Abstractions;

namespace Avalon.Server.World.UnitTests.Maps.Navigation;

public class NavigationStorageShould(ITestOutputHelper output)
{
    [Theory]
    [InlineData("Town", false)]
    [InlineData("Town", true)]
    [InlineData("Forest33", false)]
    [InlineData("Forest33", true)]
    public void Search_fresh_and_reused_queries_without_growing_storage(string map, bool unreachable)
    {
        var fixture = new NavigationStorageFixture(map);
        IDtQueryFilter filter = unreachable ? fixture.UnreachableFilter : fixture.Filter;
        var reference = new DtNavMeshQuery(fixture.Mesh);
        long[] expected = new long[fixture.PolygonCount * 4];
        long[] actual = new long[expected.Length];
        DtStatus expectedStatus = reference.FindPath(fixture.StartRef, fixture.EndRef, fixture.Start, fixture.End,
            filter, expected, out int expectedCount, expected.Length);
        Assert.True(expectedCount > 1);
        Assert.Equal(unreachable, (expectedStatus.Value & DtStatus.DT_PARTIAL_RESULT.Value) != 0);
        // Ensure that both inter-tile entry states and more than a small default pool are exercised.
        Assert.True(reference.GetNodePool().GetNodeCount() > 100);
        Assert.Contains(reference.GetNodePool().AsEnumerable(), n => n.state != 0);

        // Warm code on a separate query, never the storage whose first search is measured.
        var rehearsal = new ReusingNavMeshQuery(fixture.Mesh);
        for (int i = 0; i < 10; i++)
            rehearsal.FindPath(fixture.StartRef, fixture.EndRef, fixture.Start, fixture.End, filter, actual, out _, actual.Length);

        var query = new ReusingNavMeshQuery(fixture.Mesh);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            DtStatus status = query.FindPath(fixture.StartRef, fixture.EndRef, fixture.Start, fixture.End,
                filter, actual, out int count, actual.Length);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            output.WriteLine($"{map}, unreachable={unreachable}, attempt={attempt}: {allocated} B, {count} path polygons");
            Assert.Equal(expectedStatus.Value, status.Value);
            Assert.Equal(expectedCount, count);
            Assert.True(expected.AsSpan(0, expectedCount).SequenceEqual(actual.AsSpan(0, count)));
            Assert.Equal(0, allocated);
        }
    }

    [Theory]
    [InlineData("Town")]
    [InlineData("Forest33")]
    public void Move_across_a_fresh_surface_without_growing_storage(string map)
    {
        var fixture = new NavigationStorageFixture(map);
        long[] expected = new long[fixture.PolygonCount];
        long[] actual = new long[expected.Length];
        var reference = new DtNavMeshQuery(fixture.Mesh);
        DtStatus expectedStatus = reference.MoveAlongSurface(fixture.StartRef, fixture.Start, fixture.End, fixture.Filter,
            out RcVec3f expectedPosition, expected, out int expectedCount, expected.Length);
        Assert.True(expectedCount > 1);
        var rehearsal = new ReusingNavMeshQuery(fixture.Mesh);
        for (int i = 0; i < 10; i++)
        {
            rehearsal.MoveAlongSurfaceReusing(fixture.StartRef, fixture.Start, fixture.End, fixture.Filter,
                out _, actual, out _, actual.Length);
        }

        var query = new ReusingNavMeshQuery(fixture.Mesh);
        long before = GC.GetAllocatedBytesForCurrentThread();
        DtStatus status = query.MoveAlongSurfaceReusing(fixture.StartRef, fixture.Start, fixture.End, fixture.Filter,
            out RcVec3f position, actual, out int count, actual.Length);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"{map} first surface move: {allocated} B, {count} visited polygons");
        Assert.Equal(expectedStatus.Value, status.Value);
        Assert.Equal(expectedPosition, position);
        Assert.Equal(expectedCount, count);
        Assert.True(expected.AsSpan(0, expectedCount).SequenceEqual(actual.AsSpan(0, count)));
        Assert.Equal(0, allocated);
    }
}
