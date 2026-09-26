using Avalon.Common.ValueObjects;
using Avalon.World.Entities;
using Avalon.World.Persistence;
using Avalon.World.Public.Enums;

namespace Avalon.Server.World.UnitTests.Persistence;

/// <summary>
/// New, changed and removed state, for items and for the slots that place them, plus money's
/// dirty flag. What a save writes is decided here, and what it clears.
/// </summary>
public class SaveStateTrackerShould
{
    private static ItemInstanceId NewId() => new(Guid.CreateVersion7());

    [Fact]
    public void Start_with_nothing_to_save()
    {
        var tracker = new SaveStateTracker();

        Assert.False(tracker.HasChanges);
        Assert.False(tracker.MoneyDirty);
        Assert.Equal(SaveState.Unchanged, tracker.ItemState(NewId()));
        Assert.Equal(SaveState.Unchanged, tracker.SlotState(InventoryType.Bag, 0));
    }

    [Fact]
    public void Keep_a_new_item_new_until_it_is_first_saved()
    {
        var tracker = new SaveStateTracker();
        ItemInstanceId id = NewId();

        tracker.ItemCreated(id);
        tracker.ItemChanged(id);

        Assert.Equal(SaveState.New, tracker.ItemState(id));
    }

    [Fact]
    public void Mark_a_loaded_item_changed()
    {
        var tracker = new SaveStateTracker();
        ItemInstanceId id = NewId();

        tracker.ItemChanged(id);

        Assert.Equal(SaveState.Changed, tracker.ItemState(id));
    }

    /// <summary>Its insert may already be in flight, so the next save must delete it (Review Focus 2).</summary>
    [Fact]
    public void Mark_a_new_item_removed_rather_than_forget_it()
    {
        var tracker = new SaveStateTracker();
        ItemInstanceId id = NewId();

        tracker.ItemCreated(id);
        tracker.ItemRemoved(id);

        Assert.Equal(SaveState.Removed, tracker.ItemState(id));
    }

    [Theory]
    [InlineData(SaveState.Unchanged, false, true, SaveState.New)]
    [InlineData(SaveState.Unchanged, true, true, SaveState.Changed)]
    [InlineData(SaveState.Unchanged, true, false, SaveState.Removed)]
    [InlineData(SaveState.New, true, true, SaveState.New)]
    [InlineData(SaveState.New, true, false, SaveState.Removed)]
    [InlineData(SaveState.Changed, true, false, SaveState.Removed)]
    [InlineData(SaveState.Removed, false, true, SaveState.Changed)]
    public void Move_a_slot_between_states(SaveState start, bool before, bool after, SaveState expected)
    {
        var tracker = new SaveStateTracker();
        switch (start)
        {
            case SaveState.New: tracker.SlotChanged(InventoryType.Bag, 2, false, true); break;
            case SaveState.Changed: tracker.SlotChanged(InventoryType.Bag, 2, true, true); break;
            case SaveState.Removed: tracker.SlotChanged(InventoryType.Bag, 2, true, false); break;
        }

        tracker.SlotChanged(InventoryType.Bag, 2, before, after);

        Assert.Equal(expected, tracker.SlotState(InventoryType.Bag, 2));
    }

    [Fact]
    public void Ignore_a_slot_that_was_empty_and_stays_empty()
    {
        var tracker = new SaveStateTracker();

        tracker.SlotChanged(InventoryType.Bag, 2, false, false);

        Assert.False(tracker.HasChanges);
    }

    [Fact]
    public void Clear_what_a_committed_save_carried()
    {
        var tracker = new SaveStateTracker();
        ItemInstanceId id = NewId();
        tracker.ItemCreated(id);
        tracker.SlotChanged(InventoryType.Bag, 0, false, true);
        tracker.MoneyChanged();

        tracker.Acknowledge(tracker.TakeMarks());

        Assert.False(tracker.HasChanges);
        Assert.Equal(SaveState.Unchanged, tracker.ItemState(id));
        Assert.False(tracker.MoneyDirty);
    }

    [Fact]
    public void Keep_an_entry_that_changed_again_after_the_snapshot()
    {
        var tracker = new SaveStateTracker();
        ItemInstanceId id = NewId();
        tracker.ItemChanged(id);
        tracker.MoneyChanged();
        SaveMarks marks = tracker.TakeMarks();

        tracker.ItemChanged(id);
        tracker.MoneyChanged();
        tracker.Acknowledge(marks);

        Assert.Equal(SaveState.Changed, tracker.ItemState(id));
        Assert.True(tracker.MoneyDirty);
    }

    [Fact]
    public void Carry_each_entrys_state_in_the_snapshot()
    {
        var tracker = new SaveStateTracker();
        ItemInstanceId created = NewId(), removed = NewId();
        tracker.ItemCreated(created);
        tracker.ItemRemoved(removed);
        tracker.SlotChanged(InventoryType.Equipment, 5, true, false);

        SaveMarks marks = tracker.TakeMarks();

        Assert.Equal(SaveState.New, marks.Items[created].State);
        Assert.Equal(SaveState.Removed, marks.Items[removed].State);
        Assert.Equal(SaveState.Removed, marks.Slots[(InventoryType.Equipment, 5)].State);
        Assert.Null(marks.MoneyVersion);
    }

    [Fact]
    public void Leave_the_snapshot_untouched_by_later_changes()
    {
        var tracker = new SaveStateTracker();
        ItemInstanceId id = NewId();
        SaveMarks marks = tracker.TakeMarks();

        tracker.ItemCreated(id);

        Assert.Empty(marks.Items);
    }

    [Fact]
    public void Give_every_character_a_tracker()
    {
        Assert.NotNull(new CharacterEntity().SaveState);
    }

    [Fact]
    public void Keep_the_stats_dirty_until_the_save_that_carried_them_is_acknowledged()
    {
        var tracker = new SaveStateTracker();

        tracker.StatsChanged();
        Assert.True(tracker.StatsDirty);
        Assert.True(tracker.HasChanges);

        SaveMarks marks = tracker.TakeMarks();
        Assert.NotNull(marks.StatsVersion);

        tracker.Acknowledge(marks);
        Assert.False(tracker.StatsDirty);
        Assert.False(tracker.HasChanges);
    }

    [Fact]
    public void Keep_a_stats_change_made_while_the_save_was_in_flight()
    {
        var tracker = new SaveStateTracker();
        tracker.StatsChanged();
        SaveMarks marks = tracker.TakeMarks();

        tracker.StatsChanged();
        tracker.Acknowledge(marks);

        Assert.True(tracker.StatsDirty);
    }
}
