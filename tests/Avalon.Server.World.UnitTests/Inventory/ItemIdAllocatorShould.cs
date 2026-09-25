using System.Collections.Concurrent;
using Avalon.World.Inventory;

namespace Avalon.Server.World.UnitTests.Inventory;

/// <summary>
/// Item ids are handed out by the server so an item has its id the moment it exists in memory.
/// By the owner's ruling the id is a version-7 Guid from <c>Guid.CreateVersion7()</c>, which is
/// unique with no seed and no coordination.
/// </summary>
public class ItemIdAllocatorShould
{
    [Fact]
    public void Never_hand_out_the_same_id_twice()
    {
        var allocator = new ItemIdAllocator();

        List<Guid> ids = Enumerable.Range(0, 100_000).Select(_ => allocator.Next().Value).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Never_hand_out_the_same_id_twice_across_threads()
    {
        var allocator = new ItemIdAllocator();
        var ids = new ConcurrentBag<Guid>();

        Parallel.For(0, 8, _ =>
        {
            for (int i = 0; i < 10_000; i++)
                ids.Add(allocator.Next().Value);
        });

        Assert.Equal(80_000, ids.Distinct().Count());
    }

    [Fact]
    public void Never_hand_out_the_empty_id()
    {
        Assert.NotEqual(Guid.Empty, new ItemIdAllocator().Next().Value);
    }

    /// <summary>Time-ordered, so new rows append to the end of the primary-key index.</summary>
    [Fact]
    public void Hand_out_version_7_ids()
    {
        Assert.Equal(7, new ItemIdAllocator().Next().Value.Version);
    }
}
