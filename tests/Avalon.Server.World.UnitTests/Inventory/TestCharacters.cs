using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Server.World.UnitTests.Inventory;

/// <summary>A real CharacterEntity and a fixed template catalogue for the economy tests.</summary>
internal static class TestCharacters
{
    public static readonly ItemTemplate Potion = new()
        { Id = new ItemTemplateId(100), Name = "Potion", Class = ItemClass.Consumable, MaxStackSize = 20 };
    public static readonly ItemTemplate Sword = new()
        { Id = new ItemTemplateId(200), Name = "Sword", Class = ItemClass.Weapon, MaxStackSize = 1 };
    public static readonly ItemTemplate Relic = new()
        { Id = new ItemTemplateId(300), Name = "Relic", Class = ItemClass.Quest, MaxStackSize = 1, Flags = ItemTemplateFlags.Unique };
    public static readonly ItemTemplate Pebble = new()
        { Id = new ItemTemplateId(400), Name = "Pebble", Class = ItemClass.Junk, MaxStackSize = 0 };
    /// <summary>The widest stack a template can declare: room in a bag of these is past what a uint holds.</summary>
    public static readonly ItemTemplate Hoard = new()
        { Id = new ItemTemplateId(500), Name = "Hoard", Class = ItemClass.Junk, MaxStackSize = uint.MaxValue };

    private static readonly Dictionary<ItemTemplateId, ItemTemplate> Templates =
        new[] { Potion, Sword, Relic, Pebble, Hoard }.ToDictionary(t => t.Id);

    public static ItemTemplate? Find(ItemTemplateId id) => Templates.GetValueOrDefault(id);

    public static CharacterEntity New(uint id = 7, ulong money = 0)
    {
        var row = new Character
        {
            Id = new CharacterId(id), AccountId = new AccountId(1), Name = $"Tester{id}",
            Class = CharacterClass.Warrior, Money = money, CreationDate = DateTime.UtcNow,
        };
        return new CharacterEntity(NullLoggerFactory.Instance, row, new RegenConfiguration()) { Data = row };
    }

    public static InventoryItem Item(ushort slot, ItemTemplate template, uint count = 1, uint durability = 0, uint charges = 0) =>
        new(slot, new ItemInstanceId(Guid.CreateVersion7()), template.Id, count, durability, ItemInstanceFlags.None, charges);

    /// <summary>An inventory over this catalogue, or over <paramref name="find" /> when given one.</summary>
    public static CharacterInventoryService InventoryFor(
        CharacterEntity character, Func<ItemTemplateId, ItemTemplate?>? find = null) =>
        new(character, find ?? Find, new ItemIdAllocator());

    public static InventoryItem At(CharacterEntity character, InventoryType container, ushort slot)
    {
        Assert.True(character.Container(container).TryGet(slot, out InventoryItem item), $"{container} slot {slot} is empty");
        return item;
    }
}
