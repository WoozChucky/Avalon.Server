using Avalon.Common.ValueObjects;

namespace Avalon.World.Inventory;

/// <summary>
/// Changes one character's items. Tick thread only. All or nothing, like TrinityCore's
/// CanStoreNewItem: if the whole count cannot be applied, nothing changes. Lives in Avalon.World,
/// not the modding API; ICharacterInventory there stays read-only.
/// </summary>
public interface IInventoryService
{
    /// <summary>Whether <see cref="TryAdd" /> would succeed, changing nothing.</summary>
    InventoryAddResult CanAdd(ItemTemplateId templateId, uint count);

    /// <summary>
    /// Fills existing Bag stacks of the template up to its MaxStackSize in slot order, then the lowest
    /// free Bag slots, one new instance per stack. Never adds to Equipment or the Bank. A count of 0
    /// is Ok and changes nothing.
    /// </summary>
    InventoryAddResult TryAdd(ItemTemplateId templateId, uint count);

    /// <summary>
    /// From Bag stacks of the template, highest slot first. NotFound when the Bag holds none of it,
    /// whatever the count, including 0.
    /// </summary>
    InventoryRemoveResult TryRemove(ItemTemplateId templateId, uint count);

    /// <summary>From the one instance, wherever it sits.</summary>
    InventoryRemoveResult TryRemove(ItemInstanceId itemInstanceId, uint count);
}
