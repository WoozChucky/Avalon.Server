using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Seeding;
using Avalon.Domain.World;
using Avalon.Network.Packets.Character;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Entities;
using Avalon.World.Items;
using Avalon.World.Public.Characters;
using Microsoft.Extensions.Logging.Abstractions;
using static Avalon.Server.World.UnitTests.Inventory.EquipTemplates;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.ItemUse;

/// <summary>A use equips gear (item use) through the same rules as a drag: InventoryMove and IInventoryService.</summary>
public class ItemEquipShould
{
    private readonly CharacterEntity _character = New(7);

    private ItemUseResult Equip(byte bagSlot, ItemTemplate template, Func<ItemTemplateId, ItemTemplate?>? find = null) =>
        ItemEquip.Equip(_character, InventoryFor(_character, find ?? EquipTemplates.Find), find ?? EquipTemplates.Find, bagSlot, template,
            NullLogger.Instance);

    private void Bag(params InventoryItem[] items) => _character.Container(InventoryType.Bag).Load(items);

    private void Worn(params InventoryItem[] items) => _character.Container(InventoryType.Equipment).Load(items);

    [Fact]
    public void Swap_the_worn_item_into_the_clicked_bag_slot()
    {
        Bag(Item(2, Longsword));
        Worn(Item(EquipmentSlots.MainHand, Axe));

        Assert.Equal(ItemUseResult.Ok, Equip(2, Longsword));
        Assert.Equal(Longsword.Id, At(_character, InventoryType.Equipment, EquipmentSlots.MainHand).TemplateId);
        Assert.Equal(Axe.Id, At(_character, InventoryType.Bag, 2).TemplateId);
    }

    [Fact]
    public void Replace_the_first_finger_when_both_are_worn()
    {
        InventoryItem newRing = Item(0, Band);
        InventoryItem firstRing = Item(EquipmentSlots.Finger1, Band);
        Bag(newRing);
        Worn(firstRing, Item(EquipmentSlots.Finger2, Band));

        Assert.Equal(ItemUseResult.Ok, Equip(0, Band));
        Assert.Equal(newRing.InstanceId, At(_character, InventoryType.Equipment, EquipmentSlots.Finger1).InstanceId);
        Assert.Equal(firstRing.InstanceId, At(_character, InventoryType.Bag, 0).InstanceId);
    }

    /// <summary>
    /// #1179: a trinket fills the first trinket slot, then the second, and with both full replaces the first, as a ring
    /// does the fingers; slot 13 stays reserved.
    /// </summary>
    [Fact]
    public void Wear_a_trinket_in_either_trinket_slot()
    {
        ItemTemplate idol = ItemBaseSeed.Equipment().Single(t => t.Id.Value == 67);
        Func<ItemTemplateId, ItemTemplate?> find = id => id == idol.Id ? idol : EquipTemplates.Find(id);
        _character.Level = idol.RequiredLevel!.Value;
        InventoryItem first = Item(0, idol);
        InventoryItem second = Item(1, idol);
        InventoryItem third = Item(2, idol);
        Bag(first, second, third, Item(3, idol));

        Assert.Equal(ItemUseResult.Ok, Equip(0, idol, find));
        Assert.Equal(ItemUseResult.Ok, Equip(1, idol, find));
        Assert.Equal(ItemUseResult.Ok, Equip(2, idol, find));

        Assert.Equal(third.InstanceId, At(_character, InventoryType.Equipment, EquipmentSlots.Trinket1).InstanceId);
        Assert.Equal(second.InstanceId, At(_character, InventoryType.Equipment, EquipmentSlots.Trinket2).InstanceId);
        Assert.Equal(first.InstanceId, At(_character, InventoryType.Bag, 2).InstanceId);
        Assert.Equal(ItemRequestResult.InvalidSlot, InventoryFor(_character, find)
            .TryMove(EquipTemplates.Bag(3), Eq(EquipmentSlots.FirstReserved), count: null, bankAccessible: false));
    }

    [Fact]
    public void Move_the_off_hand_item_to_the_bag_for_a_two_hander()
    {
        Bag(Item(0, Potion), Item(1, Greatsword));
        Worn(Item(EquipmentSlots.MainHand, Axe), Item(EquipmentSlots.OffHand, Buckler));

        Assert.Equal(ItemUseResult.Ok, Equip(1, Greatsword));
        Assert.Equal(Greatsword.Id, At(_character, InventoryType.Equipment, EquipmentSlots.MainHand).TemplateId);
        Assert.False(_character.Container(InventoryType.Equipment).TryGet(EquipmentSlots.OffHand, out _));
        Assert.Equal(Axe.Id, At(_character, InventoryType.Bag, 1).TemplateId);
        Assert.Equal(Buckler.Id, At(_character, InventoryType.Bag, 2).TemplateId);   // the lowest free slot before the use
    }

    /// <summary>The clicked slot is not counted as free, so a full Bag refuses and nothing moves.</summary>
    [Fact]
    public void Refuse_a_two_hander_with_TargetFull_and_move_nothing_when_the_bag_is_full()
    {
        Bag([.. Enumerable.Range(1, 29).Select(s => Item((byte)s, Potion)), Item(0, Greatsword)]);
        Worn(Item(EquipmentSlots.OffHand, Buckler));

        Assert.Equal(ItemUseResult.TargetFull, Equip(0, Greatsword));
        Assert.Equal(Buckler.Id, At(_character, InventoryType.Equipment, EquipmentSlots.OffHand).TemplateId);
        Assert.Equal(Greatsword.Id, At(_character, InventoryType.Bag, 0).TemplateId);
        Assert.False(_character.ClientChanges.HasChanges);
    }

    [Fact]
    public void Check_a_two_handers_level_before_moving_the_off_hand()
    {
        var heavy = new ItemTemplate
        {
            Id = new ItemTemplateId(650),
            Name = "Heavy Maul",
            Class = ItemClass.Weapon,
            SubClass = ItemSubClass.TwoHanded,
            MaxStackSize = 1,
            Slot = ItemSlotType.MainHand,
            RequiredLevel = 9,
        };
        Bag(Item(0, heavy));
        Worn(Item(EquipmentSlots.OffHand, Buckler));

        Assert.Equal(ItemUseResult.LevelTooLow, Equip(0, heavy, id => id == heavy.Id ? heavy : EquipTemplates.Find(id)));
        Assert.Equal(Buckler.Id, At(_character, InventoryType.Equipment, EquipmentSlots.OffHand).TemplateId);
    }

    [Theory]
    [MemberData(nameof(Refusals))]
    public void Answer_the_move_rules_refusal(string name, ItemUseResult expected)
    {
        (ItemTemplate template, uint count) = name switch
        {
            "a stack of gear" => (StackedBand, 2u),
            "an off hand beside a worn two-hander" => (Buckler, 1u),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
        if (template == Buckler)
            Worn(Item(EquipmentSlots.MainHand, Greatsword));
        Bag(Item(0, template, count));

        Assert.Equal(expected, Equip(0, template));
        Assert.Equal(count, At(_character, InventoryType.Bag, 0).Count);
    }

    public static TheoryData<string, ItemUseResult> Refusals => new()
    {
        { "a stack of gear", ItemUseResult.NotUsable },   // no stack is ever worn
        { "an off hand beside a worn two-hander", ItemUseResult.Blocked },
    };

    /// <summary>A use of a worn item takes it off to the lowest free Bag slot, interrupting a cast just before it moves.</summary>
    [Fact]
    public void Unequip_to_the_lowest_free_bag_slot()
    {
        Bag(Item(0, Potion), Item(2, Potion));
        InventoryItem sword = Item(EquipmentSlots.MainHand, Longsword);
        Worn(sword);
        int interrupts = 0;

        Assert.Equal(ItemUseResult.Ok, Unequip(EquipmentSlots.MainHand, () => interrupts++));
        Assert.Equal(sword.InstanceId, At(_character, InventoryType.Bag, 1).InstanceId);
        Assert.False(_character.Container(InventoryType.Equipment).TryGet(EquipmentSlots.MainHand, out _));
        Assert.Equal(1, interrupts);
    }

    /// <summary>A full Bag refuses before anything happens: the item stays worn and a running cast is left alone.</summary>
    [Fact]
    public void Refuse_an_unequip_with_TargetFull_and_move_nothing_when_the_bag_is_full()
    {
        Bag([.. Enumerable.Range(0, 30).Select(s => Item((byte)s, Potion))]);
        Worn(Item(EquipmentSlots.MainHand, Longsword));
        int interrupts = 0;

        Assert.Equal(ItemUseResult.TargetFull, Unequip(EquipmentSlots.MainHand, () => interrupts++));
        Assert.Equal(Longsword.Id, At(_character, InventoryType.Equipment, EquipmentSlots.MainHand).TemplateId);
        Assert.False(_character.ClientChanges.HasChanges);
        Assert.Equal(0, interrupts);
    }

    private ItemUseResult Unequip(byte equipmentSlot, Action beforeApply) =>
        ItemEquip.Unequip(_character, InventoryFor(_character, EquipTemplates.Find), EquipTemplates.Find, equipmentSlot,
            beforeApply);
}
