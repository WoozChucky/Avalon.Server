namespace Avalon.Domain.World;

/// <summary>
/// What kind of base an item template is (#1179): an armour family in one of its six slots, a weapon type, an off-hand,
/// a piece of jewellery or a trinket. A line of tiers is every Common template of one kind. An item that is no base (a
/// consumable, a quest item) has none: <see cref="ItemTemplate.BaseKind" /> is null.
/// </summary>
public enum ItemBaseKind : byte
{
    PlateHead = 1,
    PlateShoulders = 2,
    PlateChest = 3,
    PlateHands = 4,
    PlateLegs = 5,
    PlateFeet = 6,

    LeatherHead = 7,
    LeatherShoulders = 8,
    LeatherChest = 9,
    LeatherHands = 10,
    LeatherLegs = 11,
    LeatherFeet = 12,

    ClothHead = 13,
    ClothShoulders = 14,
    ClothChest = 15,
    ClothHands = 16,
    ClothLegs = 17,
    ClothFeet = 18,

    Sword = 19,
    Mace = 20,
    Staff = 21,
    Bow = 22,
    Shield = 23,
    CasterOffHand = 24,
    Amulet = 25,
    Ring = 26,
    Trinket = 27,
}

/// <summary>Where each base kind is worn (#1179): the one mapping a template's slot must agree with.</summary>
public static class ItemBaseKindSlots
{
    /// <summary>The slot a <paramref name="kind" /> base is worn in.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A value that names no base kind.</exception>
    public static ItemSlotType SlotOf(ItemBaseKind kind) => kind switch
    {
        ItemBaseKind.PlateHead or ItemBaseKind.LeatherHead or ItemBaseKind.ClothHead => ItemSlotType.Head,
        ItemBaseKind.PlateShoulders or ItemBaseKind.LeatherShoulders or ItemBaseKind.ClothShoulders => ItemSlotType.Shoulder,
        ItemBaseKind.PlateChest or ItemBaseKind.LeatherChest or ItemBaseKind.ClothChest => ItemSlotType.Chest,
        ItemBaseKind.PlateHands or ItemBaseKind.LeatherHands or ItemBaseKind.ClothHands => ItemSlotType.Hands,
        ItemBaseKind.PlateLegs or ItemBaseKind.LeatherLegs or ItemBaseKind.ClothLegs => ItemSlotType.Legs,
        ItemBaseKind.PlateFeet or ItemBaseKind.LeatherFeet or ItemBaseKind.ClothFeet => ItemSlotType.Feet,
        ItemBaseKind.Sword or ItemBaseKind.Mace or ItemBaseKind.Staff or ItemBaseKind.Bow => ItemSlotType.MainHand,
        ItemBaseKind.Shield or ItemBaseKind.CasterOffHand => ItemSlotType.OffHand,
        ItemBaseKind.Amulet => ItemSlotType.Neck,
        ItemBaseKind.Ring => ItemSlotType.Finger,
        ItemBaseKind.Trinket => ItemSlotType.Trinket,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
