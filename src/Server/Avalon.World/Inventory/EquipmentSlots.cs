using Avalon.Domain.World;

namespace Avalon.World.Inventory;

/// <summary>
/// The one mapping from an Equipment container slot to the ItemSlotType an item must have to be
/// worn there (spec #463). The container has 14 slots; 11-13 are reserved and any request naming
/// one is refused. The numbers are not ItemSlotType's own: Gem is 8 there, but slot 8 is the second
/// finger, and a gem is a socket item that is never worn.
/// </summary>
public static class EquipmentSlots
{
    public const ushort Head = 0;
    public const ushort Neck = 1;
    public const ushort Shoulder = 2;
    public const ushort Chest = 3;
    public const ushort Hands = 4;
    public const ushort Legs = 5;
    public const ushort Feet = 6;
    public const ushort Finger1 = 7;
    public const ushort Finger2 = 8;
    public const ushort MainHand = 9;
    public const ushort OffHand = 10;

    /// <summary>The first of the reserved slots, 11, 12 and 13.</summary>
    public const ushort FirstReserved = 11;

    /// <summary>What an item worn in <paramref name="slot" /> must be, or null for a reserved or unknown slot.</summary>
    public static ItemSlotType? TypeOf(ushort slot) => slot switch
    {
        Head => ItemSlotType.Head,
        Neck => ItemSlotType.Neck,
        Shoulder => ItemSlotType.Shoulder,
        Chest => ItemSlotType.Chest,
        Hands => ItemSlotType.Hands,
        Legs => ItemSlotType.Legs,
        Feet => ItemSlotType.Feet,
        Finger1 or Finger2 => ItemSlotType.Finger,
        MainHand => ItemSlotType.MainHand,
        OffHand => ItemSlotType.OffHand,
        _ => null,
    };

    public static bool IsReserved(ushort slot) => slot >= FirstReserved;

    /// <summary>True when an item whose template Slot is <paramref name="itemSlot" /> may be worn in <paramref name="slot" />.</summary>
    public static bool Accepts(ushort slot, ItemSlotType? itemSlot) =>
        itemSlot is { } type && TypeOf(slot) == type;
}
