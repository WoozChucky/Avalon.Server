namespace Avalon.Domain.World;

/// <summary>
/// The one mapping from an Equipment container slot to the ItemSlotType an item must have to be
/// worn there (spec #463). The container has 14 slots; 11 and 12 are the trinkets (#1179) and 13 is
/// reserved: any request naming it is refused. The numbers are not ItemSlotType's own: Gem is 8 there, but slot 8 is the second
/// finger, and a gem is a socket item that is never worn. Shared by the world server and the API (#1178), so both
/// count the same worn items.
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
    public const byte Trinket1 = 11;
    public const byte Trinket2 = 12;

    /// <summary>The first of the reserved slots, 13.</summary>
    public const byte FirstReserved = 13;

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
        Trinket1 or Trinket2 => ItemSlotType.Trinket,
        _ => null,
    };

    public static bool IsReserved(byte slot) => slot >= FirstReserved;

    /// <summary>True when an item whose template Slot is <paramref name="itemSlot" /> may be worn in <paramref name="slot" />.</summary>
    public static bool Accepts(byte slot, ItemSlotType? itemSlot) =>
        itemSlot is { } type && TypeOf(slot) == type;

    // One list per answer, made once (#973): a collection expression in the switch made a list per item use.
    private static readonly byte[] s_noSlots = [];
    private static readonly byte[] s_head = [Head];
    private static readonly byte[] s_neck = [Neck];
    private static readonly byte[] s_shoulder = [Shoulder];
    private static readonly byte[] s_chest = [Chest];
    private static readonly byte[] s_hands = [Hands];
    private static readonly byte[] s_legs = [Legs];
    private static readonly byte[] s_feet = [Feet];
    private static readonly byte[] s_fingers = [Finger1, Finger2];
    private static readonly byte[] s_mainHand = [MainHand];
    private static readonly byte[] s_offHand = [OffHand];
    private static readonly byte[] s_trinkets = [Trinket1, Trinket2];

    /// <summary>
    /// The slots an item whose template Slot is <paramref name="type" /> is worn in, in the order a use fills them
    /// (item use): both fingers for a ring, both trinket slots for a trinket, none for a gem or an item with no slot. The one mapping a use reads.
    /// </summary>
    public static IReadOnlyList<byte> SlotsFor(ItemSlotType? type) => type switch
    {
        ItemSlotType.Head => s_head,
        ItemSlotType.Neck => s_neck,
        ItemSlotType.Shoulder => s_shoulder,
        ItemSlotType.Chest => s_chest,
        ItemSlotType.Hands => s_hands,
        ItemSlotType.Legs => s_legs,
        ItemSlotType.Feet => s_feet,
        ItemSlotType.Finger => s_fingers,
        ItemSlotType.MainHand => s_mainHand,
        ItemSlotType.OffHand => s_offHand,
        ItemSlotType.Trinket => s_trinkets,
        _ => s_noSlots,
    };
}
