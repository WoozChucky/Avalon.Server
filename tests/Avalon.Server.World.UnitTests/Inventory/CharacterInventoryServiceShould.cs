using Avalon.Common.ValueObjects;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Persistence;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Inventory;

/// <summary>
/// All-or-nothing adds and removals over a character's containers (spec #459 section 2). Every
/// successful change marks its save state and its client change; a refused one touches neither.
/// </summary>
public class CharacterInventoryServiceShould
{
    [Fact]
    public void Fill_partial_stacks_in_slot_order_before_taking_free_slots()
    {
        CharacterEntity character = New();
        InventoryItem low = Item(2, Potion, count: 15), high = Item(5, Potion, count: 18);
        character.Container(InventoryType.Bag).Load([low, high]);

        Assert.Equal(InventoryAddResult.Ok, InventoryFor(character).TryAdd(Potion.Id, 10));

        Assert.Equal(20u, At(character, InventoryType.Bag, 2).Count);
        Assert.Equal(20u, At(character, InventoryType.Bag, 5).Count);
        Assert.Equal(3u, At(character, InventoryType.Bag, 0).Count);
        Assert.Equal(3, character.Container(InventoryType.Bag).Items.Count);
    }

    [Fact]
    public void Start_one_new_stack_per_free_slot_lowest_first()
    {
        CharacterEntity character = New();
        character.Container(InventoryType.Bag).Load([Item(0, Sword)]);

        Assert.Equal(InventoryAddResult.Ok, InventoryFor(character).TryAdd(Potion.Id, 45));

        Assert.Equal(20u, At(character, InventoryType.Bag, 1).Count);
        Assert.Equal(20u, At(character, InventoryType.Bag, 2).Count);
        Assert.Equal(5u, At(character, InventoryType.Bag, 3).Count);
        Assert.Equal(3, new[] { 1, 2, 3 }.Select(s => At(character, InventoryType.Bag, (ushort)s).InstanceId).Distinct().Count());
    }

    [Fact]
    public void Refuse_an_add_that_does_not_fit_whole_and_change_nothing()
    {
        CharacterEntity character = New();
        List<InventoryItem> bag = Enumerable.Range(0, 29).Select(s => Item((ushort)s, Sword)).ToList();
        bag.Add(Item(29, Potion, count: 19));
        character.Container(InventoryType.Bag).Load(bag);

        Assert.Equal(InventoryAddResult.InventoryFull, InventoryFor(character).CanAdd(Potion.Id, 2));
        Assert.Equal(InventoryAddResult.InventoryFull, InventoryFor(character).TryAdd(Potion.Id, 2));

        Assert.Equal(19u, At(character, InventoryType.Bag, 29).Count);
        Assert.False(character.SaveState.HasChanges);
        Assert.False(character.ClientChanges.HasChanges);
    }

    [Fact]
    public void Never_add_to_equipment_or_the_bank()
    {
        CharacterEntity character = New();
        character.Container(InventoryType.Bag).Load(Enumerable.Range(0, 30).Select(s => Item((ushort)s, Sword)).ToList());

        Assert.Equal(InventoryAddResult.InventoryFull, InventoryFor(character).TryAdd(Potion.Id, 1));

        Assert.Empty(character.Container(InventoryType.Equipment).Items);
        Assert.Empty(character.Container(InventoryType.Bank).Items);
    }

    [Theory]
    [InlineData(InventoryType.Bag)]
    [InlineData(InventoryType.Equipment)]
    [InlineData(InventoryType.Bank)]
    public void Refuse_a_second_copy_of_a_unique_item_wherever_the_first_is(InventoryType heldIn)
    {
        CharacterEntity character = New();
        character.Container(heldIn).Load([Item(0, Relic)]);

        Assert.Equal(InventoryAddResult.UniqueAlreadyOwned, InventoryFor(character).TryAdd(Relic.Id, 1));
        Assert.False(character.SaveState.HasChanges);
    }

    [Fact]
    public void Refuse_more_than_one_copy_of_a_unique_item_in_one_add()
    {
        CharacterEntity character = New();

        Assert.Equal(InventoryAddResult.UniqueAlreadyOwned, InventoryFor(character).TryAdd(Relic.Id, 2));
        Assert.Empty(character.Container(InventoryType.Bag).Items);

        Assert.Equal(InventoryAddResult.Ok, InventoryFor(character).TryAdd(Relic.Id, 1));
    }

    [Fact]
    public void Report_an_unknown_template()
    {
        Assert.Equal(InventoryAddResult.UnknownTemplate, InventoryFor(New()).TryAdd(new ItemTemplateId(9999), 1));
    }

    /// <summary>Review Focus 4.</summary>
    [Fact]
    public void Treat_a_max_stack_size_of_zero_as_one()
    {
        CharacterEntity character = New();

        Assert.Equal(InventoryAddResult.Ok, InventoryFor(character).TryAdd(Pebble.Id, 3));

        Assert.Equal(3, character.Container(InventoryType.Bag).Items.Count);
        Assert.All(character.Container(InventoryType.Bag).Items, item => Assert.Equal(1u, item.Count));
    }

    /// <summary>Review Focus 4: a stack above the template's maximum (a lowered template) is never topped up.</summary>
    [Fact]
    public void Leave_an_overfull_stack_alone()
    {
        CharacterEntity character = New();
        character.Container(InventoryType.Bag).Load([Item(0, Potion, count: 25)]);

        Assert.Equal(InventoryAddResult.Ok, InventoryFor(character).TryAdd(Potion.Id, 1));

        Assert.Equal(25u, At(character, InventoryType.Bag, 0).Count);
        Assert.Equal(1u, At(character, InventoryType.Bag, 1).Count);
    }

    /// <summary>Review Focus 3.</summary>
    [Fact]
    public void Refuse_a_count_no_bag_could_hold_without_wrapping()
    {
        CharacterEntity character = New();

        Assert.Equal(InventoryAddResult.InventoryFull, InventoryFor(character).TryAdd(Potion.Id, uint.MaxValue));

        Assert.Empty(character.Container(InventoryType.Bag).Items);
    }

    [Fact]
    public void Change_nothing_for_a_count_of_zero()
    {
        CharacterEntity character = New();

        Assert.Equal(InventoryAddResult.Ok, InventoryFor(character).TryAdd(Potion.Id, 0));
        Assert.Equal(InventoryRemoveResult.NotFound, InventoryFor(character).TryRemove(Potion.Id, 0));

        Assert.False(character.SaveState.HasChanges);
    }

    [Fact]
    public void Answer_CanAdd_without_changing_anything()
    {
        CharacterEntity character = New();

        Assert.Equal(InventoryAddResult.Ok, InventoryFor(character).CanAdd(Potion.Id, 5));

        Assert.Empty(character.Container(InventoryType.Bag).Items);
        Assert.False(character.SaveState.HasChanges);
    }

    [Fact]
    public void Remove_by_template_from_the_highest_bag_slot_first()
    {
        CharacterEntity character = New();
        character.Container(InventoryType.Bag).Load([Item(1, Potion, count: 20), Item(4, Potion, count: 20), Item(7, Potion, count: 5)]);

        Assert.Equal(InventoryRemoveResult.Ok, InventoryFor(character).TryRemove(Potion.Id, 12));

        Assert.False(character.Container(InventoryType.Bag).TryGet(7, out _));
        Assert.Equal(13u, At(character, InventoryType.Bag, 4).Count);
        Assert.Equal(20u, At(character, InventoryType.Bag, 1).Count);
    }

    [Fact]
    public void Report_not_enough_and_remove_nothing()
    {
        CharacterEntity character = New();
        character.Container(InventoryType.Bag).Load([Item(1, Potion, count: 20), Item(4, Potion, count: 5)]);

        Assert.Equal(InventoryRemoveResult.NotEnough, InventoryFor(character).TryRemove(Potion.Id, 26));

        Assert.Equal(20u, At(character, InventoryType.Bag, 1).Count);
        Assert.Equal(5u, At(character, InventoryType.Bag, 4).Count);
        Assert.False(character.SaveState.HasChanges);
    }

    [Fact]
    public void Look_only_in_the_bag_when_removing_by_template()
    {
        CharacterEntity character = New();
        character.Container(InventoryType.Equipment).Load([Item(0, Sword)]);

        Assert.Equal(InventoryRemoveResult.NotFound, InventoryFor(character).TryRemove(Sword.Id, 1));
        Assert.Single(character.Container(InventoryType.Equipment).Items);
    }

    [Fact]
    public void Remove_by_instance_in_part_then_whole()
    {
        CharacterEntity character = New();
        InventoryItem potion = Item(3, Potion, count: 20);
        character.Container(InventoryType.Bag).Load([potion]);
        CharacterInventoryService inventory = InventoryFor(character);

        Assert.Equal(InventoryRemoveResult.NotEnough, inventory.TryRemove(potion.InstanceId, 21));
        Assert.Equal(InventoryRemoveResult.Ok, inventory.TryRemove(potion.InstanceId, 5));
        Assert.Equal(15u, At(character, InventoryType.Bag, 3).Count);
        Assert.Equal(InventoryRemoveResult.Ok, inventory.TryRemove(potion.InstanceId, 15));
        Assert.False(character.Container(InventoryType.Bag).TryGet(3, out _));
        Assert.Equal(InventoryRemoveResult.NotFound, inventory.TryRemove(potion.InstanceId, 1));
    }

    [Fact]
    public void Find_an_instance_in_any_container()
    {
        CharacterEntity character = New();
        InventoryItem banked = Item(9, Sword);
        character.Container(InventoryType.Bank).Load([banked]);

        Assert.Equal(InventoryRemoveResult.Ok, InventoryFor(character).TryRemove(banked.InstanceId, 1));
        Assert.Empty(character.Container(InventoryType.Bank).Items);
    }

    [Fact]
    public void Mark_what_each_change_does_to_items_and_slots()
    {
        CharacterEntity character = New();
        InventoryItem loaded = Item(0, Potion, count: 10);
        character.Container(InventoryType.Bag).Load([loaded]);
        CharacterInventoryService inventory = InventoryFor(character);

        inventory.TryAdd(Potion.Id, 15);   // tops slot 0 up to 20, then 5 into slot 1
        InventoryItem created = At(character, InventoryType.Bag, 1);

        Assert.Equal(SaveState.Changed, character.SaveState.ItemState(loaded.InstanceId));
        Assert.Equal(SaveState.Unchanged, character.SaveState.SlotState(InventoryType.Bag, 0));
        Assert.Equal(SaveState.New, character.SaveState.ItemState(created.InstanceId));
        Assert.Equal(SaveState.New, character.SaveState.SlotState(InventoryType.Bag, 1));

        inventory.TryRemove(loaded.InstanceId, 20);

        Assert.Equal(SaveState.Removed, character.SaveState.ItemState(loaded.InstanceId));
        Assert.Equal(SaveState.Removed, character.SaveState.SlotState(InventoryType.Bag, 0));
    }

    [Fact]
    public void Record_the_slots_the_client_must_hear_about_but_never_the_bank()
    {
        CharacterEntity character = New();
        InventoryItem banked = Item(4, Sword);
        character.Container(InventoryType.Bank).Load([banked]);
        CharacterInventoryService inventory = InventoryFor(character);

        inventory.TryRemove(banked.InstanceId, 1);
        Assert.False(character.ClientChanges.HasChanges);

        inventory.TryAdd(Potion.Id, 1);
        Assert.Equal((InventoryType.Bag, (ushort)0), Assert.Single(character.ClientChanges.Slots));
    }

    [Fact]
    public void Give_a_new_weapon_the_durability_character_creation_gives_one()
    {
        CharacterEntity character = New();

        InventoryFor(character).TryAdd(Sword.Id, 1);

        Assert.Equal(ItemInstanceDefaults.InitialDurability(Sword), At(character, InventoryType.Bag, 0).Durability);
        Assert.Equal(42u, At(character, InventoryType.Bag, 0).Durability);
    }

    /// <summary>
    /// The tracker cannot enforce this itself: ItemChanged on a Removed item would bring a deleted
    /// row back on the next save. Nothing after a removal may touch the removed instance again.
    /// </summary>
    [Fact]
    public void Never_mark_a_removed_item_changed_again()
    {
        CharacterEntity character = New();
        InventoryItem potion = Item(0, Potion, count: 5);
        InventoryItem other = Item(1, Potion, count: 5);
        character.Container(InventoryType.Bag).Load([potion, other]);
        CharacterInventoryService inventory = InventoryFor(character);

        Assert.Equal(InventoryRemoveResult.Ok, inventory.TryRemove(potion.InstanceId, 5));

        Assert.Equal(InventoryRemoveResult.NotFound, inventory.TryRemove(potion.InstanceId, 1));
        Assert.Equal(InventoryRemoveResult.NotFound, inventory.TryRemove(potion.InstanceId, 0));
        Assert.Equal(InventoryAddResult.Ok, inventory.TryAdd(Potion.Id, 20));
        Assert.Equal(InventoryRemoveResult.Ok, inventory.TryRemove(Potion.Id, 3));

        Assert.Equal(SaveState.Removed, character.SaveState.ItemState(potion.InstanceId));
        Assert.NotEqual(potion.InstanceId, At(character, InventoryType.Bag, 0).InstanceId);
    }
}
