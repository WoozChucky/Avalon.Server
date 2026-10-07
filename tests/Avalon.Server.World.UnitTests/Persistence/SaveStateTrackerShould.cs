using Avalon.Common.ValueObjects;
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
    public void Keep_a_new_item_new_until_it_is_first_saved()
    {
        var tracker = new SaveStateTracker();
        ItemInstanceId id = NewId();

        tracker.ItemCreated(id);
        tracker.ItemChanged(id);

        Assert.Equal(SaveState.New, tracker.ItemState(id));
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
    public void Keep_a_stats_change_made_while_the_save_was_in_flight()
    {
        var tracker = new SaveStateTracker();
        tracker.StatsChanged();
        SaveMarks marks = tracker.TakeMarks();

        tracker.StatsChanged();
        tracker.Acknowledge(marks);

        Assert.True(tracker.StatsDirty);
    }

    [Fact]
    public void Keep_a_pvp_change_made_while_the_save_was_in_flight()
    {
        var tracker = new SaveStateTracker();
        tracker.PvpChanged();
        SaveMarks marks = tracker.TakeMarks();

        tracker.PvpChanged();
        tracker.Acknowledge(marks);

        Assert.True(tracker.PvpDirty);
        Assert.True(tracker.HasChanges);
        tracker.Acknowledge(tracker.TakeMarks());
        Assert.False(tracker.PvpDirty);
        Assert.False(tracker.HasChanges);
    }

    [Fact]
    public void Keep_a_quest_marked_again_after_its_save_was_taken()
    {
        var tracker = new SaveStateTracker();
        tracker.QuestChanged(5);
        SaveMarks marks = tracker.TakeMarks();
        tracker.QuestChanged(5);

        tracker.Acknowledge(marks);

        Assert.True(tracker.HasChanges);
        tracker.Acknowledge(tracker.TakeMarks());
        Assert.False(tracker.HasChanges);
    }

    [Fact]
    public void Keep_an_ignore_entry_marked_again_after_its_save_was_taken()
    {
        var tracker = new SaveStateTracker();
        tracker.IgnoreChanged(7);
        SaveMarks marks = tracker.TakeMarks();
        Assert.True(marks.Ignores!.ContainsKey(7));
        tracker.IgnoreChanged(7);

        tracker.Acknowledge(marks);

        Assert.True(tracker.HasChanges);
        tracker.Acknowledge(tracker.TakeMarks());
        Assert.False(tracker.HasChanges);
    }

    [Fact]
    public void Clear_the_aura_mark_only_once_the_save_that_carried_it_commits()
    {
        var tracker = new SaveStateTracker();
        tracker.AurasChanged();
        SaveMarks first = tracker.TakeMarks();
        tracker.AurasChanged();   // changed again while the first save is in flight

        tracker.Acknowledge(first);
        Assert.True(tracker.AurasDirty);

        tracker.Acknowledge(tracker.TakeMarks());
        Assert.False(tracker.AurasDirty);
        Assert.False(tracker.HasChanges);
    }
}
