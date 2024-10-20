// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using Avalon.Domain.Characters;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Entities;

public class CharacterInventoryContainer(ILoggerFactory loggerFactory, InventoryType type) : ICharacterInventory
{
    private readonly ILogger<CharacterInventoryContainer> _logger =
        loggerFactory.CreateLogger<CharacterInventoryContainer>();

    private ushort MaxSlots => type switch
    {
        InventoryType.Equipment => 14,
        InventoryType.Bag => 30,
        InventoryType.Bank => 30,
        _ => throw new ArgumentOutOfRangeException()
    };

    public void Load(IReadOnlyCollection<object> items)
    {
        List<CharacterInventory> castedItems = items.Cast<CharacterInventory>().ToList();
        _logger.LogInformation("Loading {Count} items into {Type} inventory", castedItems.Count, type);
    }
}
