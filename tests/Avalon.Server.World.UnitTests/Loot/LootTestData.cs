using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Loot;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Server.World.UnitTests.Loot;

/// <summary>
/// Loot tables, item templates and creature templates for the loot tests. The Potion, Sword and
/// Pebble templates are the economy tests' own, so a drop picked up in a loot test lands in a bag
/// that knows them.
/// </summary>
internal static class LootTestData
{
    /// <summary>Stacks to 20.</summary>
    public static ItemTemplate Potion => TestCharacters.Potion;

    /// <summary>Does not stack.</summary>
    public static ItemTemplate Sword => TestCharacters.Sword;

    public static readonly ItemTemplate Staff = new()
        { Id = new ItemTemplateId(201), Name = "Staff", Class = ItemClass.Weapon, MaxStackSize = 1 };

    /// <summary>Declares no stack size at all; treated as 1.</summary>
    public static ItemTemplate Pebble => TestCharacters.Pebble;

    public static IReadOnlyCollection<ItemTemplate> Items { get; } = [Potion, Sword, Staff, Pebble];

    public static LootTableEntry Item(int sequence, ItemTemplate item, float chance = 100f, int? group = null,
        int min = 1, int max = 1) => new()
    {
        Sequence = sequence, ItemTemplateId = item.Id, Chance = chance, GroupId = group, MinCount = min, MaxCount = max
    };

    public static LootTableEntry Reference(int sequence, int table, float chance = 100f, int? group = null) => new()
    {
        Sequence = sequence, ReferenceTableId = new LootTableId(table), Chance = chance, GroupId = group,
        MinCount = 1, MaxCount = 1
    };

    public static LootTable Table(int id, params LootTableEntry[] entries)
    {
        foreach (LootTableEntry entry in entries)
            entry.LootTableId = new LootTableId(id);
        return new LootTable { Id = new LootTableId(id), Name = $"table-{id}", Entries = [.. entries] };
    }

    public static LootCatalog Catalog(params LootTable[] tables) => new(tables, NullLoggerFactory.Instance);

    public static CreatureTemplate BoarTemplate(int? lootTableId, int minGold = 0, int maxGold = 0) => new()
    {
        Id = new CreatureTemplateId(4),
        Name = "Thornback Boar",
        LootTableId = lootTableId is { } id ? new LootTableId(id) : null,
        MinGold = minGold,
        MaxGold = maxGold,
    };
}
