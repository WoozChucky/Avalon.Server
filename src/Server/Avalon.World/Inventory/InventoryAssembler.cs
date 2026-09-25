using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Inventory;

/// <summary>
/// Puts a CharacterInventory row together with the ItemInstance it points at. The row says where
/// an item sits and the instance says what it is. Both now live in the Character database, joined
/// by a foreign key, but they are still read as two queries and correlated here.
/// </summary>
public static class InventoryAssembler
{
    public static IReadOnlyDictionary<InventoryType, List<InventoryItem>> Assemble(
        IReadOnlyCollection<CharacterInventory> rows,
        IReadOnlyCollection<ItemInstance> instances,
        ILogger logger)
    {
        Dictionary<ItemInstanceId, ItemInstance> instanceById = instances
            .GroupBy(instance => instance.Id)
            .ToDictionary(group => group.Key, group => group.First());

        Dictionary<InventoryType, List<InventoryItem>> assembled = Enum
            .GetValues<InventoryType>()
            .ToDictionary(container => container, _ => new List<InventoryItem>());

        HashSet<(InventoryType, ushort)> taken = [];

        foreach (CharacterInventory row in rows)
        {
            if (!assembled.TryGetValue(row.Container, out List<InventoryItem>? container))
            {
                logger.LogWarning(
                    "Skipping inventory row in slot {Slot}: {Container} is not a known container",
                    row.Slot, (ushort)row.Container);
                continue;
            }

            // An item the row points at but that no longer exists would otherwise become a slot
            // holding template zero, which a client renders as something rather than as nothing.
            if (!instanceById.TryGetValue(row.ItemId, out ItemInstance? instance))
            {
                logger.LogWarning(
                    "Skipping inventory row in {Container} slot {Slot}: item instance {ItemId} not found",
                    row.Container, row.Slot, row.ItemId.Value);
                continue;
            }

            if (!taken.Add((row.Container, row.Slot)))
            {
                logger.LogWarning(
                    "Skipping duplicate inventory row for {Container} slot {Slot}: item instance {ItemId}",
                    row.Container, row.Slot, row.ItemId.Value);
                continue;
            }

            container.Add(new InventoryItem(
                row.Slot,
                instance.Id,
                instance.TemplateId,
                instance.Count,
                instance.Durability,
                instance.Flags));
        }

        return assembled;
    }
}
