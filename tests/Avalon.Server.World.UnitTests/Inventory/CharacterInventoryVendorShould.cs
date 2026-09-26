using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Persistence;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Inventory;

/// <summary>
/// The two inventory operations vendors need (spec #432). TakeOut hands a sold item over whole or
/// split. TryAddInstance puts a bought-back item back under its own id. Both mark SaveState and
/// ClientChanges as every other change does.
/// </summary>
public class CharacterInventoryVendorShould
{
    [Fact]
    public void Take_a_whole_stack_out_and_mark_the_item_and_its_slot_removed()
    {
        CharacterEntity character = New();
        InventoryItem potion = Item(2, Potion, count: 5);
        character.Container(InventoryType.Bag).Load([potion]);

        InventoryItem taken = InventoryFor(character).TakeOut(new SlotRef(InventoryType.Bag, 2), 5);

        Assert.Equal(potion, taken);
        Assert.False(character.Container(InventoryType.Bag).TryGet(2, out _));
        Assert.Equal(SaveState.Removed, character.SaveState.ItemState(potion.InstanceId));
        Assert.Equal(SaveState.Removed, character.SaveState.SlotState(InventoryType.Bag, 2));
        Assert.Contains((InventoryType.Bag, (ushort)2), character.ClientChanges.Slots);
    }

    [Fact]
    public void Take_part_of_a_stack_out_as_a_copy_with_a_new_id()
    {
        CharacterEntity character = New();
        InventoryItem potion = Item(2, Potion, count: 5, charges: 3);
        character.Container(InventoryType.Bag).Load([potion]);

        InventoryItem taken = InventoryFor(character).TakeOut(new SlotRef(InventoryType.Bag, 2), 2);

        Assert.NotEqual(potion.InstanceId, taken.InstanceId);
        Assert.Equal(potion with { InstanceId = taken.InstanceId, Count = 2 }, taken);
        Assert.Equal(3u, At(character, InventoryType.Bag, 2).Count);
        Assert.Equal(SaveState.Changed, character.SaveState.ItemState(potion.InstanceId));
        Assert.Equal(SaveState.Unchanged, character.SaveState.SlotState(InventoryType.Bag, 2));
        // The copy is in no container and nothing saves it until it is bought back.
        Assert.Equal(SaveState.Unchanged, character.SaveState.ItemState(taken.InstanceId));
    }

    [Fact]
    public void Throw_for_a_take_out_the_slot_cannot_cover_and_change_nothing()
    {
        CharacterEntity character = New();
        character.Container(InventoryType.Bag).Load([Item(2, Potion, count: 5)]);
        CharacterInventoryService inventory = InventoryFor(character);

        Assert.Throws<InvalidOperationException>(() => inventory.TakeOut(new SlotRef(InventoryType.Bag, 3), 1));
        Assert.Throws<InvalidOperationException>(() => inventory.TakeOut(new SlotRef(InventoryType.Bag, 2), 6));
        Assert.Throws<InvalidOperationException>(() => inventory.TakeOut(new SlotRef(InventoryType.Bag, 2), 0));

        Assert.False(character.SaveState.HasChanges);
        Assert.False(character.ClientChanges.HasChanges);
    }

    [Fact]
    public void Put_the_exact_instance_in_the_lowest_free_bag_slot_and_mark_it_new()
    {
        CharacterEntity character = New();
        character.Container(InventoryType.Bag).Load([Item(0, Potion, count: 3)]);
        var sold = new InventoryItem(7, new ItemInstanceId(Guid.CreateVersion7()), Sword.Id, 1, 42, ItemInstanceFlags.None, 2);

        Assert.Equal(InventoryAddResult.Ok, InventoryFor(character).TryAddInstance(sold));

        Assert.Equal(sold with { Slot = 1 }, At(character, InventoryType.Bag, 1));
        Assert.Equal(SaveState.New, character.SaveState.ItemState(sold.InstanceId));
        Assert.Equal(SaveState.New, character.SaveState.SlotState(InventoryType.Bag, 1));
        Assert.Contains((InventoryType.Bag, (ushort)1), character.ClientChanges.Slots);
    }

    /// <summary>A sold potion comes back as its own instance, never merged into the stack beside it.</summary>
    [Fact]
    public void Never_merge_an_instance_into_a_stack()
    {
        CharacterEntity character = New();
        InventoryItem stack = Item(0, Potion, count: 3);
        character.Container(InventoryType.Bag).Load([stack]);

        InventoryFor(character).TryAddInstance(Item(5, Potion, count: 2));

        Assert.Equal(3u, At(character, InventoryType.Bag, 0).Count);
        Assert.Equal(2u, At(character, InventoryType.Bag, 1).Count);
    }

    [Fact]
    public void Mark_a_removed_item_new_again_when_it_comes_back()
    {
        CharacterEntity character = New();
        InventoryItem sword = Item(4, Sword);
        character.Container(InventoryType.Bag).Load([sword]);
        CharacterInventoryService inventory = InventoryFor(character);

        InventoryItem taken = inventory.TakeOut(new SlotRef(InventoryType.Bag, 4), 1);
        Assert.Equal(SaveState.Removed, character.SaveState.ItemState(sword.InstanceId));

        inventory.TryAddInstance(taken);

        Assert.Equal(SaveState.New, character.SaveState.ItemState(sword.InstanceId));
        // The lowest free slot is 0, so the slot it left stays removed. VendorSaveRoundTripShould
        // covers an item coming back into the slot it left, whose slot mark becomes Changed.
        Assert.Equal(SaveState.New, character.SaveState.SlotState(InventoryType.Bag, 0));
        Assert.Equal(SaveState.Removed, character.SaveState.SlotState(InventoryType.Bag, 4));
    }

    [Fact]
    public void Refuse_an_instance_when_no_bag_slot_is_free_and_change_nothing()
    {
        CharacterEntity character = New();
        character.Container(InventoryType.Bag).Load(Enumerable.Range(0, 30).Select(s => Item((ushort)s, Sword)).ToList());

        Assert.Equal(InventoryAddResult.InventoryFull, InventoryFor(character).TryAddInstance(Item(0, Potion)));

        Assert.False(character.SaveState.HasChanges);
        Assert.False(character.ClientChanges.HasChanges);
    }

    /// <summary>
    /// The instance path keeps the unique rule on its own (#432), so a caller that skipped
    /// VendorRules still cannot leave two copies of a unique item.
    /// </summary>
    [Fact]
    public void Refuse_a_unique_instance_already_owned_anywhere_and_change_nothing()
    {
        CharacterEntity character = New();
        character.Container(InventoryType.Bank).Load([Item(3, Relic)]);

        Assert.Equal(InventoryAddResult.UniqueAlreadyOwned, InventoryFor(character).TryAddInstance(Item(0, Relic)));

        Assert.Empty(character.Container(InventoryType.Bag).Items);
        Assert.False(character.SaveState.HasChanges);
        Assert.False(character.ClientChanges.HasChanges);
    }

    [Fact]
    public void Add_a_unique_instance_nobody_owns()
    {
        CharacterEntity character = New();
        InventoryItem relic = Item(9, Relic);

        Assert.Equal(InventoryAddResult.Ok, InventoryFor(character).TryAddInstance(relic));

        Assert.Equal(relic with { Slot = 0 }, At(character, InventoryType.Bag, 0));
    }

    /// <summary>An item whose template a reload removed is still the player's own, so it comes back.</summary>
    [Fact]
    public void Add_an_instance_whose_template_is_gone()
    {
        CharacterEntity character = New();
        var orphan = new InventoryItem(0, new ItemInstanceId(Guid.CreateVersion7()), new ItemTemplateId(9999), 1, 0, ItemInstanceFlags.None);

        Assert.Equal(InventoryAddResult.Ok, InventoryFor(character).TryAddInstance(orphan));

        Assert.Equal(orphan, At(character, InventoryType.Bag, 0));
    }

    [Fact]
    public void Throw_for_an_instance_already_held()
    {
        CharacterEntity character = New();
        InventoryItem banked = Item(3, Sword);
        character.Container(InventoryType.Bank).Load([banked]);

        Assert.Throws<InvalidOperationException>(() => InventoryFor(character).TryAddInstance(banked));
        Assert.False(character.SaveState.HasChanges);
    }
}
