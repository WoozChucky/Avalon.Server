using System.ComponentModel.DataAnnotations;
using Avalon.Common.ValueObjects;
using Avalon.World.Public.Enums;

namespace Avalon.Domain.World;

public class ItemTemplate : IDbEntity<ItemTemplateId>
{
    [Key]
    public ItemTemplateId Id { get; set; }

    public string Name { get; set; }

    public ItemClass Class { get; set; }

    public ItemSubClass SubClass { get; set; }

    public ItemTemplateFlags Flags { get; set; }

    public bool Stackable => MaxStackSize > 1;

    public uint MaxStackSize { get; set; }

    public uint DisplayId { get; set; }

    public ItemRarity Rarity { get; set; }

    public uint BuyPrice { get; set; }

    public uint SellPrice { get; set; }

    public ItemSlotType? Slot { get; set; }

    public List<CharacterClass> AllowedClasses { get; set; } =
        [CharacterClass.Warrior, CharacterClass.Wizard, CharacterClass.Hunter, CharacterClass.Healer]; // Default to all classes

    public ushort? ItemPower { get; set; }

    public ushort? RequiredLevel { get; set; }

    public uint? DamageMin1 { get; set; }

    public uint? DamageMax1 { get; set; }

    public DamageType? DamageType1 { get; set; }

    public uint? DamageMin2 { get; set; }

    public uint? DamageMax2 { get; set; }

    public DamageType? DamageType2 { get; set; }

    public StatType? StatType1 { get; set; }

    public uint? StatValue1 { get; set; }

    public StatType? StatType2 { get; set; }

    public uint? StatValue2 { get; set; }

    public StatType? StatType3 { get; set; }

    public uint? StatValue3 { get; set; }

    public StatType? StatType4 { get; set; }

    public uint? StatValue4 { get; set; }

    public StatType? StatType5 { get; set; }

    public uint? StatValue5 { get; set; }

    public StatType? StatType6 { get; set; }

    public uint? StatValue6 { get; set; }

    public StatType? StatType7 { get; set; }

    public uint? StatValue7 { get; set; }

    public StatType? StatType8 { get; set; }

    public uint? StatValue8 { get; set; }

    public StatType? StatType9 { get; set; }

    public uint? StatValue9 { get; set; }

    public StatType? StatType10 { get; set; }

    public uint? StatValue10 { get; set; }

    // For future use
    private void ValidateSubClass()
    {
        if (!IsValidSubClassForClass(Class, SubClass))
        {
            throw new ArgumentException($"Invalid subclass {SubClass} for class {Class}");
        }
    }

    // For future use
    private bool IsValidSubClassForClass(ItemClass itemClass, ItemSubClass itemSubClass)
    {
        return itemClass switch
        {
            ItemClass.Consumable => itemSubClass is ItemSubClass.Potion or ItemSubClass.Food or ItemSubClass.Scroll,
            ItemClass.Weapon => itemSubClass is ItemSubClass.OneHanded or ItemSubClass.TwoHanded or ItemSubClass.Ranged,
            ItemClass.Armor => itemSubClass is ItemSubClass.Shield or ItemSubClass.Helmet or ItemSubClass.Chest or ItemSubClass.Legs or ItemSubClass.Boots or ItemSubClass.Gloves,
            ItemClass.Quest => itemSubClass == ItemSubClass.QuestItem,
            ItemClass.Crafting => itemSubClass == ItemSubClass.CraftingMaterial,
            ItemClass.Junk => itemSubClass == ItemSubClass.JunkItem,
            _ => false,
        };
    }
}

public enum StatType
{
    Stamina,
    Strength,
    Agility,
    Intellect,
    Armor,
    BlockPct,
    DodgePct,
    CritPct,
    AttackDamage,
    AbilityDamage,
    Health,
    Power,
    AttackSpeed,
    MovementSpeed,
}

public enum DamageType
{
    Physical,
    Poison,
    Fire,
    Lightning,
}

[Flags]
public enum ItemTemplateFlags
{
    None = 0,
    Attunned = 1,
    AccountAttunned = 2,
    Unique = 4,
    UniqueEquipped = 8,
    AttuneOnPickup = 16,
    AttuneOnEquip = 32,
    AttuneOnUse = 64,
    AttuneOnAccount = 128,
    NoSell = 256,
    NoDestroy = 512,
    NoTrade = 1024,
}

public enum ItemSlotType : ushort
{
    Head,
    Neck,
    Shoulder,
    Chest,
    Hands,
    Legs,
    Feet,
    Finger,
    Gem,
    MainHand,
    OffHand
}

public enum ItemRarity
{
    Junk,
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary
}

public enum ItemClass
{
    Consumable,
    Weapon,
    Armor,
    Quest,
    Crafting,
    Junk
}

public enum ItemSubClass
{
    // Consumable Subclasses
    Potion = 0,
    Food = 1,
    Scroll = 2,
    // Weapon Subclasses
    OneHanded = 100,
    TwoHanded = 101,
    Ranged = 102,
    // Armor Subclasses
    Shield = 200,
    Helmet = 201,
    Chest = 202,
    Legs = 203,
    Boots = 204,
    Gloves = 205,
    // Quest Subclasses
    QuestItem = 300,
    // Crafting Subclasses
    CraftingMaterial = 400,
    // Junk Subclasses
    JunkItem = 500
}
