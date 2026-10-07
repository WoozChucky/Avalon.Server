namespace Avalon.World.Public.Characters;

public interface ICharacterInventory
{
    /// <summary>Replaces the whole contents. Items in slots the container does not have are dropped.</summary>
    void Load(IReadOnlyCollection<InventoryItem> items);

    IReadOnlyCollection<InventoryItem> Items { get; }

    bool TryGet(ushort slot, out InventoryItem item);
}
