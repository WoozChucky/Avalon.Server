using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.World.Public.Characters;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Inventory;

/// <summary>
/// Puts a CharacterInventory row together with the ItemInstance it points at. The row says where
/// an item sits and the instance says what it is. Both now live in the Character database, joined
/// by a foreign key, but they are still read as two queries and correlated here.
/// </summary>
public static class InventoryAssembler
{
    /// <summary>Every container, read off the enum once rather than at each login (#883).</summary>
    private static readonly InventoryType[] s_containers = Enum.GetValues<InventoryType>();

    public static IReadOnlyDictionary<InventoryType, List<InventoryItem>> Assemble(
        IReadOnlyCollection<CharacterInventory> rows,
        IReadOnlyCollection<ItemInstance> instances,
        ILogger logger)
    {
        // The first instance read under an id is the one kept.
        var instanceById = new Dictionary<ItemInstanceId, ItemInstance>(instances.Count);
        foreach (ItemInstance instance in instances)
            instanceById.TryAdd(instance.Id, instance);

        var assembled = new Dictionary<InventoryType, List<InventoryItem>>(s_containers.Length);
        foreach (InventoryType container in s_containers)
            assembled[container] = [];

        HashSet<(InventoryType, byte)> taken = [];

        foreach (CharacterInventory row in rows)
        {
            if (!assembled.TryGetValue(row.Container, out List<InventoryItem>? container))
            {
                logger.LogWarning(
                    "Skipping inventory row in slot {Slot}: {Container} is not a known container",
                    row.Slot, (byte)row.Container);
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
                instance.Flags,
                instance.Rarity,
                instance.ItemLevel,
                instance.RareName,
                instance.Charges));
        }

        return assembled;
    }
}
