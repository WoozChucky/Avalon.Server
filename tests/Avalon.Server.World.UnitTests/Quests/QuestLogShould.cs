using Avalon.Domain.Characters;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Entities;
using Avalon.World.Quests;

namespace Avalon.Server.World.UnitTests.Quests;

/// <summary>
/// QuestLog (#433) holds one character's quests in memory. Every change marks the save and the client, and
/// loading marks neither, as the inventory does.
/// </summary>
public class QuestLogShould
{
    private static readonly DateTime s_now = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    private static CharacterEntity Character() => TestCharacters.New(1);

    [Fact]
    public void Load_without_marking_the_save_or_the_client()
    {
        CharacterEntity c = Character();

        c.Quests.Load(new Avalon.Database.Character.Repositories.CharacterQuestRows(
            [new CharacterQuest { CharacterId = 1, QuestId = 5, State = CharacterQuestState.Active, Stage = 1, AcceptedAt = s_now }],
            [new CharacterQuestObjective { CharacterId = 1, QuestId = 5, ObjectiveId = 51, Progress = 3 }],
            [new CharacterCompletedQuest { CharacterId = 1, QuestId = 4, CompletedAt = s_now }]));

        Assert.Equal(1, c.Quests.ActiveCount);
        Assert.Equal(3u, c.Quests.Get(5)!.ProgressOf(51));
        Assert.Equal(1, c.Quests.Get(5)!.Stage);
        Assert.True(c.Quests.IsCompleted(4));
        Assert.False(c.SaveState.HasChanges);
        Assert.Empty(c.Quests.ClientChanges);
    }

    [Fact]
    public void Change_nothing_when_the_progress_is_already_that_value()
    {
        CharacterEntity c = Character();
        ActiveQuest quest = c.Quests.Start(5, s_now);
        c.Quests.SetProgress(quest, 51, 2);
        c.Quests.ClearClientChanges();
        int version = c.Quests.Version;

        Assert.False(c.Quests.SetProgress(quest, 51, 2));
        Assert.Empty(c.Quests.ClientChanges);
        Assert.Equal(version, c.Quests.Version);
    }

    /// <summary>The saved row keeps the first completion time (an insert that skips an existing row), so memory does too.</summary>
    [Fact]
    public void Keep_the_first_completion_time()
    {
        CharacterEntity c = Character();
        c.Quests.Complete(5, s_now);

        c.Quests.Complete(5, s_now.AddHours(1));

        Assert.Equal(s_now, c.Quests.CompletedAt(5));
    }
}
