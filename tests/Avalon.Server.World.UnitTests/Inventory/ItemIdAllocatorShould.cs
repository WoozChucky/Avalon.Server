using Avalon.World.Inventory;

namespace Avalon.Server.World.UnitTests.Inventory;

/// <summary>
/// Item ids are handed out by the server so an item has its id the moment it exists in memory.
/// By the owner's ruling the id is a version-7 Guid from <c>Guid.CreateVersion7()</c>, which is
/// unique with no seed and no coordination.
/// </summary>
public class ItemIdAllocatorShould
{
    /// <summary>Time-ordered, so new rows append to the end of the primary-key index.</summary>
    [Fact]
    public void Hand_out_version_7_ids()
    {
        Assert.Equal(7, new ItemIdAllocator().Next().Value.Version);
    }
}
