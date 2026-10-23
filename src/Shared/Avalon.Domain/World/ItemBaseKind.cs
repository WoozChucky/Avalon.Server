namespace Avalon.Domain.World;

/// <summary>
/// What kind of base an item template is (#1179): an armour family in one of its six slots, a weapon type, an off-hand,
/// a piece of jewellery or a trinket. A line of tiers is every Common template of one kind. 0 for an item that is no
/// base (a consumable, a quest item).
/// </summary>
public enum ItemBaseKind : byte
{
    None = 0,

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

/// <summary>A set of <see cref="ItemBaseKind" />s, one bit per kind: <c>1 &lt;&lt; (kind - 1)</c>.</summary>
[Flags]
public enum ItemBaseKinds : uint
{
    None = 0,

    PlateHead = 1u << 0,
    PlateShoulders = 1u << 1,
    PlateChest = 1u << 2,
    PlateHands = 1u << 3,
    PlateLegs = 1u << 4,
    PlateFeet = 1u << 5,

    LeatherHead = 1u << 6,
    LeatherShoulders = 1u << 7,
    LeatherChest = 1u << 8,
    LeatherHands = 1u << 9,
    LeatherLegs = 1u << 10,
    LeatherFeet = 1u << 11,

    ClothHead = 1u << 12,
    ClothShoulders = 1u << 13,
    ClothChest = 1u << 14,
    ClothHands = 1u << 15,
    ClothLegs = 1u << 16,
    ClothFeet = 1u << 17,

    Sword = 1u << 18,
    Mace = 1u << 19,
    Staff = 1u << 20,
    Bow = 1u << 21,
    Shield = 1u << 22,
    CasterOffHand = 1u << 23,
    Amulet = 1u << 24,
    Ring = 1u << 25,
    Trinket = 1u << 26,
}
