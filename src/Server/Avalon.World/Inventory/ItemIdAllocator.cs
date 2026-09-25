using Avalon.Common.ValueObjects;

namespace Avalon.World.Inventory;

/// <summary>
/// Hands out item instance ids on the server, as TrinityCore's GUID generator does, so an item has
/// its id the moment it exists in memory and a save never waits on the database to name it.
/// </summary>
public interface IItemIdAllocator
{
    ItemInstanceId Next();
}

/// <summary>
/// <see cref="ItemInstanceId" /> wraps a Guid: the column is <c>uuid</c> and
/// <c>ItemSlotDto.ItemInstanceId</c> crosses the wire as <c>.bcl.Guid</c>. By the owner's ruling,
/// ids are <c>Guid.CreateVersion7()</c> and the EF mapping uses <c>ValueGeneratedNever</c>, so the
/// database never generates one. A version-7 Guid is unique with no seed and no coordination
/// between world servers, and it is time-ordered, so inserts append to the primary-key index.
/// </summary>
public sealed class ItemIdAllocator : IItemIdAllocator
{
    public ItemInstanceId Next() => new(Guid.CreateVersion7());
}
