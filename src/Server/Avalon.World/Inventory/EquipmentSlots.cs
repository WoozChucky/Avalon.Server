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
    public const byte Head = 0;
    public const byte Neck = 1;
    public const byte Shoulder = 2;
    public const byte Chest = 3;
    public const byte Hands = 4;
    public const byte Legs = 5;
    public const byte Feet = 6;
    public const byte Finger1 = 7;
    public const byte Finger2 = 8;
    public const byte MainHand = 9;
    public const byte OffHand = 10;

    /// <summary>The first of the reserved slots, 11, 12 and 13.</summary>
    public const byte FirstReserved = 11;

    /// <summary>What an item worn in <paramref name="slot" /> must be, or null for a reserved or unknown slot.</summary>
    public static ItemSlotType? TypeOf(byte slot) => slot switch
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

    public static bool IsReserved(byte slot) => slot >= FirstReserved;

    /// <summary>True when an item whose template Slot is <paramref name="itemSlot" /> may be worn in <paramref name="slot" />.</summary>
    public static bool Accepts(byte slot, ItemSlotType? itemSlot) =>
        itemSlot is { } type && TypeOf(slot) == type;

    private static readonly byte[] s_noSlots = [];
    private static readonly byte[] s_fingers = [Finger1, Finger2];

    /// <summary>
    /// The slots an item whose template Slot is <paramref name="type" /> is worn in, in the order a use fills them
    /// (item use): both fingers for a ring, none for a gem or an item with no slot. The one mapping a use reads.
    /// </summary>
    public static IReadOnlyList<byte> SlotsFor(ItemSlotType? type) => type switch
    {
        ItemSlotType.Head => [Head],
        ItemSlotType.Neck => [Neck],
        ItemSlotType.Shoulder => [Shoulder],
        ItemSlotType.Chest => [Chest],
        ItemSlotType.Hands => [Hands],
        ItemSlotType.Legs => [Legs],
        ItemSlotType.Feet => [Feet],
        ItemSlotType.Finger => s_fingers,
        ItemSlotType.MainHand => [MainHand],
        ItemSlotType.OffHand => [OffHand],
        _ => s_noSlots,
    };
}
