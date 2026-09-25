// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Entities;

public class CharacterInventoryContainer(ILoggerFactory loggerFactory, InventoryType type) : ICharacterInventory
{
    private readonly ILogger<CharacterInventoryContainer> _logger =
        loggerFactory.CreateLogger<CharacterInventoryContainer>();

    private readonly Dictionary<ushort, InventoryItem> _items = [];

    private ushort MaxSlots => type switch
    {
        InventoryType.Equipment => 14,
        InventoryType.Bag => 30,
        InventoryType.Bank => 30,
        _ => throw new ArgumentOutOfRangeException()
    };

    public IReadOnlyCollection<InventoryItem> Items => _items.Values;

    /// <summary>How many slots this container has.</summary>
    public ushort Capacity => MaxSlots;

    /// <summary>The empty slots, lowest first.</summary>
    public IEnumerable<ushort> FreeSlots()
    {
        for (ushort slot = 0; slot < MaxSlots; slot++)
        {
            if (!_items.ContainsKey(slot))
                yield return slot;
        }
    }

    /// <summary>
    /// Puts <paramref name="item" /> in its slot, replacing what was there. Only
    /// CharacterInventoryService calls this, because it also marks the save state and the client
    /// change; a write that goes around it is neither saved nor sent.
    /// </summary>
    public void Put(InventoryItem item)
    {
        if (item.Slot >= MaxSlots)
            throw new ArgumentOutOfRangeException(nameof(item), item.Slot, $"{type} has {MaxSlots} slots");

        _items[item.Slot] = item;
    }

    /// <summary>Empties a slot. Same caller rule as <see cref="Put" />.</summary>
    public bool Remove(ushort slot) => _items.Remove(slot);

    public void Load(IReadOnlyCollection<InventoryItem> items)
    {
        _items.Clear();

        foreach (InventoryItem item in items)
        {
            if (item.Slot >= MaxSlots)
            {
                _logger.LogWarning(
                    "Dropping item in slot {Slot} of {Type}: the container has {MaxSlots} slots",
                    item.Slot, type, MaxSlots);
                continue;
            }

            _items[item.Slot] = item;
        }

        _logger.LogInformation("Loaded {Count} items into {Type} inventory", _items.Count, type);
    }

    public bool TryGet(ushort slot, out InventoryItem item) => _items.TryGetValue(slot, out item);
}
