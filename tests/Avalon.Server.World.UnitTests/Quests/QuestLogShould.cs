using Avalon.Domain.Characters;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Entities;
using Avalon.World.Persistence;
using Avalon.World.Quests;
using Xunit;

namespace Avalon.Server.World.UnitTests.Quests;

/// <summary>
/// QuestLog (#433) holds one character's quests in memory. Every change marks the save and the client, and
/// loading marks neither, as the inventory does.
/// </summary>
public class QuestLogShould
{
    private static readonly DateTime Now = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    private static CharacterEntity Character() => TestCharacters.New(1);

    [Fact]
    public void Load_without_marking_the_save_or_the_client()
    {
        CharacterEntity c = Character();

        c.Quests.Load(new Avalon.Database.Character.Repositories.CharacterQuestRows(
            [new CharacterQuest { CharacterId = 1, QuestId = 5, State = CharacterQuestState.Active, Stage = 1, AcceptedAt = Now }],
            [new CharacterQuestObjective { CharacterId = 1, QuestId = 5, ObjectiveId = 51, Progress = 3 }],
            [new CharacterCompletedQuest { CharacterId = 1, QuestId = 4, CompletedAt = Now }]));

        Assert.Equal(1, c.Quests.ActiveCount);
        Assert.Equal(3u, c.Quests.Get(5)!.ProgressOf(51));
        Assert.Equal(1, c.Quests.Get(5)!.Stage);
        Assert.True(c.Quests.IsCompleted(4));
        Assert.False(c.SaveState.HasChanges);
        Assert.Empty(c.Quests.ClientChanges);
    }

    [Fact]
    public void Mark_the_save_and_the_client_when_a_quest_starts()
    {
        CharacterEntity c = Character();

        ActiveQuest quest = c.Quests.Start(5, Now);

        Assert.Equal((CharacterQuestState.Active, 0, Now), (quest.State, quest.Stage, quest.AcceptedAt));
        Assert.True(c.SaveState.HasChanges);
        Assert.Equal(QuestClientChange.Accepted, c.Quests.ClientChanges[5]);
    }

    [Fact]
    public void Keep_an_accept_an_accept_when_progress_follows_in_the_same_tick()
    {
        CharacterEntity c = Character();
        ActiveQuest quest = c.Quests.Start(5, Now);

        Assert.True(c.Quests.SetProgress(quest, 51, 2));

        Assert.Equal(QuestClientChange.Accepted, c.Quests.ClientChanges[5]);
        c.Quests.ClearClientChanges();
        Assert.True(c.Quests.SetProgress(quest, 51, 3));
        Assert.Equal(QuestClientChange.Progress, c.Quests.ClientChanges[5]);
    }

    [Fact]
    public void Change_nothing_when_the_progress_is_already_that_value()
    {
        CharacterEntity c = Character();
        ActiveQuest quest = c.Quests.Start(5, Now);
        c.Quests.SetProgress(quest, 51, 2);
        c.Quests.ClearClientChanges();
        int version = c.Quests.Version;

        Assert.False(c.Quests.SetProgress(quest, 51, 2));
        Assert.Empty(c.Quests.ClientChanges);
        Assert.Equal(version, c.Quests.Version);
    }

    [Fact]
    public void Remove_an_abandoned_quest_and_forget_its_progress()
    {
        CharacterEntity c = Character();
        c.Quests.SetProgress(c.Quests.Start(5, Now), 51, 2);

        Assert.True(c.Quests.Remove(5));

        Assert.False(c.Quests.IsActive(5));
        Assert.False(c.Quests.IsCompleted(5));
        Assert.Equal(QuestClientChange.Removed, c.Quests.ClientChanges[5]);
        Assert.Equal(0u, c.Quests.Start(5, Now).ProgressOf(51));
    }

    [Fact]
    public void Move_a_turned_in_quest_to_the_completed_set()
    {
        CharacterEntity c = Character();
        c.Quests.Start(5, Now);

        c.Quests.Complete(5, Now);

        Assert.False(c.Quests.IsActive(5));
        Assert.True(c.Quests.IsCompleted(5));
        Assert.Equal(Now, c.Quests.CompletedAt(5));
        Assert.Equal(QuestClientChange.Completed, c.Quests.ClientChanges[5]);
    }

    /// <summary>The saved row keeps the first completion time (an insert that skips an existing row), so memory does too.</summary>
    [Fact]
    public void Keep_the_first_completion_time()
    {
        CharacterEntity c = Character();
        c.Quests.Complete(5, Now);

        c.Quests.Complete(5, Now.AddHours(1));

        Assert.Equal(Now, c.Quests.CompletedAt(5));
    }

    [Fact]
    public void Queue_lines_for_the_flush()
    {
        CharacterEntity c = Character();

        c.Quests.Say("Boars slain: 1/6");

        Assert.Equal(["Boars slain: 1/6"], c.Quests.PendingLines);
    }
}
