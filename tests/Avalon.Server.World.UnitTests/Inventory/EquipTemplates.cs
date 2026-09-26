using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public.Enums;

namespace Avalon.Server.World.UnitTests.Inventory;

/// <summary>
/// Equipment for the move, stats and handler tests, on top of TestCharacters' bag items. Every
/// template not given an AllowedClasses list allows all four classes, as ItemTemplate defaults.
/// </summary>
internal static class EquipTemplates
{
    public static readonly ItemTemplate Longsword = new()
    {
        Id = new ItemTemplateId(600), Name = "Longsword", Class = ItemClass.Weapon, SubClass = ItemSubClass.OneHanded,
        MaxStackSize = 1, Slot = ItemSlotType.MainHand, AllowedClasses = [CharacterClass.Warrior], RequiredLevel = 1,
        StatType1 = StatType.Strength, StatValue1 = 3, StatType2 = StatType.AttackSpeed, StatValue2 = 13,
    };

    public static readonly ItemTemplate Axe = new()
    {
        Id = new ItemTemplateId(601), Name = "Axe", Class = ItemClass.Weapon, SubClass = ItemSubClass.OneHanded,
        MaxStackSize = 1, Slot = ItemSlotType.MainHand, AllowedClasses = [CharacterClass.Warrior], RequiredLevel = 1,
    };

    public static readonly ItemTemplate Greatsword = new()
    {
        Id = new ItemTemplateId(602), Name = "Greatsword", Class = ItemClass.Weapon, SubClass = ItemSubClass.TwoHanded,
        MaxStackSize = 1, Slot = ItemSlotType.MainHand, AllowedClasses = [CharacterClass.Warrior], RequiredLevel = 1,
        StatType1 = StatType.Strength, StatValue1 = 6,
    };

    public static readonly ItemTemplate Buckler = new()
    {
        Id = new ItemTemplateId(603), Name = "Buckler", Class = ItemClass.Armor, SubClass = ItemSubClass.Shield,
        MaxStackSize = 1, Slot = ItemSlotType.OffHand, AllowedClasses = [CharacterClass.Warrior],
        StatType1 = StatType.Armor, StatValue1 = 5,
    };

    public static readonly ItemTemplate IronHelm = new()
    {
        Id = new ItemTemplateId(604), Name = "Iron Helm", Class = ItemClass.Armor, SubClass = ItemSubClass.Helmet,
        MaxStackSize = 1, Slot = ItemSlotType.Head, AllowedClasses = [CharacterClass.Warrior], RequiredLevel = 3,
        StatType1 = StatType.Stamina, StatValue1 = 4,
    };

    public static readonly ItemTemplate Circlet = new()
    {
        Id = new ItemTemplateId(605), Name = "Circlet", Class = ItemClass.Armor, SubClass = ItemSubClass.Helmet,
        MaxStackSize = 1, Slot = ItemSlotType.Head, AllowedClasses = [CharacterClass.Wizard],
    };

    public static readonly ItemTemplate Band = new()
    {
        Id = new ItemTemplateId(606), Name = "Band", Class = ItemClass.Armor, MaxStackSize = 1, Slot = ItemSlotType.Finger,
    };

    /// <summary>A ring that stacks, which no real item does, so a merge into Equipment can be asked for.</summary>
    public static readonly ItemTemplate StackedBand = new()
    {
        Id = new ItemTemplateId(607), Name = "Stacked Band", Class = ItemClass.Armor, MaxStackSize = 5, Slot = ItemSlotType.Finger,
    };

    /// <summary>ItemSlotType.Gem is 8, the same number as the second finger slot. It is not equipment.</summary>
    public static readonly ItemTemplate Ruby = new()
    {
        Id = new ItemTemplateId(608), Name = "Ruby", Class = ItemClass.Crafting, MaxStackSize = 1, Slot = ItemSlotType.Gem,
    };

    public static readonly ItemTemplate Heirloom = new()
    {
        Id = new ItemTemplateId(609), Name = "Heirloom", Class = ItemClass.Quest, MaxStackSize = 1,
        Flags = ItemTemplateFlags.NoDestroy,
    };

    /// <summary>The seeded Barkplate Chestguard's stats: Strength 2, Armor 8, Stamina 2.</summary>
    public static readonly ItemTemplate Chestguard = new()
    {
        Id = new ItemTemplateId(610), Name = "Chestguard", Class = ItemClass.Armor, SubClass = ItemSubClass.Chest,
        MaxStackSize = 1, Slot = ItemSlotType.Chest, AllowedClasses = [CharacterClass.Warrior], RequiredLevel = 1,
        StatType1 = StatType.Strength, StatValue1 = 2, StatType2 = StatType.Armor, StatValue2 = 8,
        StatType3 = StatType.Stamina, StatValue3 = 2,
    };

    /// <summary>Flat pool and damage stats, for every class.</summary>
    public static readonly ItemTemplate VigorAmulet = new()
    {
        Id = new ItemTemplateId(611), Name = "Amulet of Vigor", Class = ItemClass.Armor, MaxStackSize = 1,
        Slot = ItemSlotType.Neck,
        StatType1 = StatType.Health, StatValue1 = 15, StatType2 = StatType.Power, StatValue2 = 20,
        StatType3 = StatType.AttackDamage, StatValue3 = 3, StatType4 = StatType.CritPct, StatValue4 = 2,
    };

    /// <summary>Not in any catalogue: stands for an item whose template a reload removed.</summary>
    public static readonly ItemTemplate Ghost = new()
    {
        Id = new ItemTemplateId(9999), Name = "Ghost", Class = ItemClass.Junk, MaxStackSize = 1,
    };

    private static readonly Dictionary<ItemTemplateId, ItemTemplate> Mine = new[]
    {
        Longsword, Axe, Greatsword, Buckler, IronHelm, Circlet, Band, StackedBand, Ruby, Heirloom, Chestguard, VigorAmulet,
    }.ToDictionary(t => t.Id);

    /// <summary>These, then TestCharacters' Potion, Sword, Relic, Pebble and Hoard. Never Ghost.</summary>
    public static ItemTemplate? Find(ItemTemplateId id) => Mine.GetValueOrDefault(id) ?? TestCharacters.Find(id);

    public static IReadOnlyCollection<ItemTemplate> All =>
    [
        .. Mine.Values, TestCharacters.Potion, TestCharacters.Sword, TestCharacters.Relic, TestCharacters.Pebble,
        TestCharacters.Hoard,
    ];

    public static CharacterInventoryService InventoryFor(CharacterEntity character) =>
        TestCharacters.InventoryFor(character, Find);

    public static SlotRef Bag(ushort slot) => new(InventoryType.Bag, slot);

    public static SlotRef Eq(ushort slot) => new(InventoryType.Equipment, slot);

    public static SlotRef Vault(ushort slot) => new(InventoryType.Bank, slot);
}
