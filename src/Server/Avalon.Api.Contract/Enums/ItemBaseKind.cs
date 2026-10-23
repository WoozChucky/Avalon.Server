using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

/// <summary>
/// What kind of base an item template is (#1179): an armour family in one of its six slots, a weapon type, an off-hand,
/// a piece of jewellery or a trinket. A consumable or a quest item is no base and has none (null).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
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
