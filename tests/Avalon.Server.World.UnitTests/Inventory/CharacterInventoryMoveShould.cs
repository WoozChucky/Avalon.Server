using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Character;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Persistence;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Inventory.EquipTemplates;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Inventory;

/// <summary>
/// Applying a decided move (spec #463): all or nothing, with the save states and client changes
/// the #459 operations mark, so saving and the client update need nothing new.
/// </summary>
public class CharacterInventoryMoveShould
{
    [Fact]
    public void Move_the_same_instance_and_change_only_the_two_slot_rows()
    {
        CharacterEntity character = New();
        InventoryItem potion = Item(0, Potion, count: 5);
        character.Container(InventoryType.Bag).Load([potion]);

        Assert.Equal(ItemRequestResult.Ok, EquipTemplates.InventoryFor(character).TryMove(Bag(0), Bag(3), null, false));

        Assert.False(character.Container(InventoryType.Bag).TryGet(0, out _));
        Assert.Equal(potion with { Slot = 3 }, At(character, InventoryType.Bag, 3));
        Assert.Equal(SaveState.Unchanged, character.SaveState.ItemState(potion.InstanceId));
        Assert.Equal(SaveState.Removed, character.SaveState.SlotState(InventoryType.Bag, 0));
        Assert.Equal(SaveState.New, character.SaveState.SlotState(InventoryType.Bag, 3));
        Assert.Equal(
            [(InventoryType.Bag, (ushort)0), (InventoryType.Bag, (ushort)3)],
            character.ClientChanges.Slots.OrderBy(s => s.Slot).ToArray());
    }

    [Fact]
    public void Swap_two_items_and_change_both_slot_rows()
    {
        CharacterEntity character = New();
        InventoryItem potion = Item(0, Potion, count: 5), sword = Item(1, Sword);
        character.Container(InventoryType.Bag).Load([potion, sword]);

        Assert.Equal(ItemRequestResult.Ok, EquipTemplates.InventoryFor(character).TryMove(Bag(0), Bag(1), null, false));

        Assert.Equal(sword.InstanceId, At(character, InventoryType.Bag, 0).InstanceId);
        Assert.Equal(potion.InstanceId, At(character, InventoryType.Bag, 1).InstanceId);
        Assert.Equal(SaveState.Changed, character.SaveState.SlotState(InventoryType.Bag, 0));
        Assert.Equal(SaveState.Changed, character.SaveState.SlotState(InventoryType.Bag, 1));
        Assert.Equal(SaveState.Unchanged, character.SaveState.ItemState(potion.InstanceId));
        Assert.Equal(SaveState.Unchanged, character.SaveState.ItemState(sword.InstanceId));
        Assert.Equal(2, character.ClientChanges.Slots.Count);
    }

    [Fact]
    public void Split_into_a_new_instance_whose_id_comes_from_the_allocator()
    {
        CharacterEntity character = New();
        InventoryItem potion = Item(0, Potion, count: 5, durability: 7, charges: 2);
        character.Container(InventoryType.Bag).Load([potion]);
        var fresh = new ItemInstanceId(Guid.CreateVersion7());
        IItemIdAllocator ids = Substitute.For<IItemIdAllocator>();
        ids.Next().Returns(fresh);

        Assert.Equal(ItemRequestResult.Ok,
            new CharacterInventoryService(character, EquipTemplates.Find, ids).TryMove(Bag(0), Bag(4), 2, false));

        Assert.Equal(3u, At(character, InventoryType.Bag, 0).Count);
        InventoryItem split = At(character, InventoryType.Bag, 4);
        Assert.Equal(fresh, split.InstanceId);
        Assert.Equal((2u, 7u, 2u, Potion.Id), (split.Count, split.Durability, split.Charges, split.TemplateId));
        Assert.Equal(SaveState.Changed, character.SaveState.ItemState(potion.InstanceId));
        Assert.Equal(SaveState.New, character.SaveState.ItemState(fresh));
        Assert.Equal(SaveState.New, character.SaveState.SlotState(InventoryType.Bag, 4));
        Assert.Equal(SaveState.Unchanged, character.SaveState.SlotState(InventoryType.Bag, 0));
    }

    [Fact]
    public void Merge_part_of_a_stack_and_leave_the_rest_behind()
    {
        CharacterEntity character = New();
        InventoryItem low = Item(0, Potion, count: 15), high = Item(1, Potion, count: 10);
        character.Container(InventoryType.Bag).Load([low, high]);

        Assert.Equal(ItemRequestResult.Ok, EquipTemplates.InventoryFor(character).TryMove(Bag(0), Bag(1), null, false));

        Assert.Equal(20u, At(character, InventoryType.Bag, 1).Count);
        Assert.Equal(5u, At(character, InventoryType.Bag, 0).Count);
        Assert.Equal(SaveState.Changed, character.SaveState.ItemState(low.InstanceId));
        Assert.Equal(SaveState.Changed, character.SaveState.ItemState(high.InstanceId));
    }

    [Fact]
    public void Merge_a_whole_stack_and_remove_the_emptied_instance()
    {
        CharacterEntity character = New();
        InventoryItem low = Item(0, Potion, count: 5), high = Item(1, Potion, count: 10);
        character.Container(InventoryType.Bag).Load([low, high]);

        Assert.Equal(ItemRequestResult.Ok, EquipTemplates.InventoryFor(character).TryMove(Bag(0), Bag(1), null, false));

        Assert.Equal(15u, At(character, InventoryType.Bag, 1).Count);
        Assert.False(character.Container(InventoryType.Bag).TryGet(0, out _));
        Assert.Equal(SaveState.Removed, character.SaveState.ItemState(low.InstanceId));
        Assert.Equal(SaveState.Removed, character.SaveState.SlotState(InventoryType.Bag, 0));
    }

    [Fact]
    public void Equip_and_unequip_through_the_same_move()
    {
        CharacterEntity character = New();
        InventoryItem sword = Item(0, Longsword);
        character.Container(InventoryType.Bag).Load([sword]);
        CharacterInventoryService inventory = EquipTemplates.InventoryFor(character);

        Assert.Equal(ItemRequestResult.Ok, inventory.TryMove(Bag(0), Eq(EquipmentSlots.MainHand), null, false));
        Assert.Equal(sword.InstanceId, At(character, InventoryType.Equipment, EquipmentSlots.MainHand).InstanceId);

        Assert.Equal(ItemRequestResult.Ok, inventory.TryMove(Eq(EquipmentSlots.MainHand), Bag(5), null, false));
        Assert.Empty(character.Container(InventoryType.Equipment).Items);
        Assert.Equal(sword.InstanceId, At(character, InventoryType.Bag, 5).InstanceId);
    }

    [Fact]
    public void Change_nothing_for_a_refused_move()
    {
        CharacterEntity character = New();
        character.Container(InventoryType.Bag).Load([Item(0, Potion, count: 5), Item(1, Potion, count: 20)]);

        Assert.Equal(ItemRequestResult.TargetFull, EquipTemplates.InventoryFor(character).TryMove(Bag(0), Bag(1), null, false));

        Assert.Equal(5u, At(character, InventoryType.Bag, 0).Count);
        Assert.Equal(20u, At(character, InventoryType.Bag, 1).Count);
        Assert.False(character.SaveState.HasChanges);
        Assert.False(character.ClientChanges.HasChanges);
    }

    [Fact]
    public void Deposit_into_the_bank_when_it_is_accessible()
    {
        CharacterEntity character = New();
        InventoryItem potion = Item(0, Potion, count: 5);
        character.Container(InventoryType.Bag).Load([potion]);

        Assert.Equal(ItemRequestResult.Ok, EquipTemplates.InventoryFor(character).TryMove(Bag(0), Vault(2), null, true));

        Assert.Equal(potion.InstanceId, At(character, InventoryType.Bank, 2).InstanceId);
        Assert.Equal(SaveState.New, character.SaveState.SlotState(InventoryType.Bank, 2));
    }

    [Fact]
    public void Destroy_part_of_a_stack_then_the_rest()
    {
        CharacterEntity character = New();
        InventoryItem potion = Item(0, Potion, count: 5);
        character.Container(InventoryType.Bag).Load([potion]);
        CharacterInventoryService inventory = EquipTemplates.InventoryFor(character);

        Assert.Equal(ItemRequestResult.Ok, inventory.TryDestroy(Bag(0), 2, false));
        Assert.Equal(3u, At(character, InventoryType.Bag, 0).Count);
        Assert.Equal(SaveState.Changed, character.SaveState.ItemState(potion.InstanceId));

        Assert.Equal(ItemRequestResult.Ok, inventory.TryDestroy(Bag(0), null, false));
        Assert.False(character.Container(InventoryType.Bag).TryGet(0, out _));
        Assert.Equal(SaveState.Removed, character.SaveState.ItemState(potion.InstanceId));
        Assert.Equal(SaveState.Removed, character.SaveState.SlotState(InventoryType.Bag, 0));
    }

    [Fact]
    public void Change_nothing_for_a_refused_destroy()
    {
        CharacterEntity character = New();
        character.Container(InventoryType.Bag).Load([Item(0, Heirloom)]);

        Assert.Equal(ItemRequestResult.CannotDestroy, EquipTemplates.InventoryFor(character).TryDestroy(Bag(0), null, false));

        Assert.True(character.Container(InventoryType.Bag).TryGet(0, out _));
        Assert.False(character.SaveState.HasChanges);
        Assert.False(character.ClientChanges.HasChanges);
    }

    [Fact]
    public void Move_swap_take_off_and_destroy_an_item_whose_template_is_gone()
    {
        CharacterEntity character = New();
        InventoryItem ghost = Item(0, Ghost, count: 3), sword = Item(1, Sword), worn = Item(EquipmentSlots.Finger2, Ghost);
        character.Container(InventoryType.Bag).Load([ghost, sword]);
        character.Container(InventoryType.Equipment).Load([worn]);
        CharacterInventoryService inventory = EquipTemplates.InventoryFor(character);

        Assert.Equal(ItemRequestResult.Ok, inventory.TryMove(Bag(0), Bag(2), null, false));
        Assert.Equal(ghost with { Slot = 2 }, At(character, InventoryType.Bag, 2));

        Assert.Equal(ItemRequestResult.Ok, inventory.TryMove(Bag(2), Bag(1), null, false));
        Assert.Equal(ghost.InstanceId, At(character, InventoryType.Bag, 1).InstanceId);
        Assert.Equal(sword.InstanceId, At(character, InventoryType.Bag, 2).InstanceId);

        Assert.Equal(ItemRequestResult.Ok, inventory.TryMove(Bag(1), Vault(0), null, true));
        Assert.Equal(ghost.InstanceId, At(character, InventoryType.Bank, 0).InstanceId);

        Assert.Equal(ItemRequestResult.Ok, inventory.TryMove(Eq(EquipmentSlots.Finger2), Bag(5), null, false));
        Assert.Empty(character.Container(InventoryType.Equipment).Items);
        Assert.Equal(worn.InstanceId, At(character, InventoryType.Bag, 5).InstanceId);

        Assert.Equal(ItemRequestResult.Ok, inventory.TryDestroy(Vault(0), 1, true));
        Assert.Equal(2u, At(character, InventoryType.Bank, 0).Count);
        Assert.Equal(ItemRequestResult.Ok, inventory.TryDestroy(Bag(5), null, false));
        Assert.False(character.Container(InventoryType.Bag).TryGet(5, out _));
        Assert.Equal(SaveState.Removed, character.SaveState.ItemState(worn.InstanceId));
        Assert.Equal(SaveState.Changed, character.SaveState.ItemState(ghost.InstanceId));
    }
}
