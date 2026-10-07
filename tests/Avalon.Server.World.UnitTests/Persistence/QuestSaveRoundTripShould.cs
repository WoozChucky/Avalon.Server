using Avalon.Database;
using Avalon.Database.Character;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.Server.World.UnitTests.Handlers;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Entities;
using Avalon.World.Persistence;
using Avalon.World.Quests;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Server.World.UnitTests.Persistence;

/// <summary>
/// Quests (#433) through the real save path on SQLite and back through the select-time load: an active quest with
/// its stage and counts, an abandoned one gone, and a turned-in one completed.
/// </summary>
public sealed class QuestSaveRoundTripShould : IDisposable
{
    private static readonly DateTime s_now = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    private readonly SqliteDatabase<CharacterDbContext> _database = SqliteDatabase.Characters();
    private readonly CharacterSaveRepository _saves;
    private readonly CharacterQuestRepository _quests;

    public QuestSaveRoundTripShould()
    {
        using (CharacterDbContext context = _database.CreateDbContext())
            context.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");
        _saves = new CharacterSaveRepository(new DbTransactionRunner<CharacterDbContext>(_database));
        _quests = new CharacterQuestRepository(_database);
    }

    public void Dispose() => _database.Dispose();

    private async Task<CharacterEntity> StoredCharacterAsync()
    {
        CharacterEntity character = TestCharacters.New(id: 1);
        await using CharacterDbContext context = _database.CreateDbContext();
        context.Characters.Add(character.Data!);
        await context.SaveChangesAsync();
        await AdmittedCharacter.BindAsync(_database, character);
        return character;
    }

    private async Task SaveAsync(CharacterEntity character)
    {
        var snapshot = CharacterSaveSnapshot.Take(character);
        await _saves.WriteAsync([snapshot.Batch]);
        character.SaveState.Acknowledge(snapshot.Marks);
    }

    private async Task<QuestLog> ReloadAsync()
    {
        var log = new QuestLog(new SaveStateTracker());
        log.Load(await _quests.GetByCharacterIdAsync(1));
        return log;
    }

    [Fact]
    public async Task Round_trip_an_active_quest_with_its_stage_and_counts()
    {
        CharacterEntity character = await StoredCharacterAsync();
        ActiveQuest quest = character.Quests.Start(5, s_now);
        character.Quests.SetProgress(quest, 51, 2);
        character.Quests.SetStage(quest, 1);
        character.Quests.SetProgress(quest, 52, 1);

        await SaveAsync(character);
        QuestLog loaded = await ReloadAsync();

        ActiveQuest back = Assert.Single(loaded.Active);
        Assert.Equal((5u, 1, CharacterQuestState.Active, s_now), (back.QuestId, back.Stage, back.State, back.AcceptedAt));
        Assert.Equal(2u, back.ProgressOf(51));
        Assert.Equal(1u, back.ProgressOf(52));
        Assert.False(character.SaveState.HasChanges);
    }

    [Fact]
    public async Task Delete_an_abandoned_quest_and_its_counts()
    {
        CharacterEntity character = await StoredCharacterAsync();
        character.Quests.SetProgress(character.Quests.Start(5, s_now), 51, 2);
        await SaveAsync(character);

        character.Quests.Remove(5);
        await SaveAsync(character);

        QuestLog loaded = await ReloadAsync();
        Assert.Empty(loaded.Active);
        await using CharacterDbContext read = _database.CreateDbContext();
        Assert.Empty(read.CharacterQuestObjectives);
    }

    [Fact]
    public async Task Store_a_turned_in_quest_as_completed_and_idempotently()
    {
        CharacterEntity character = await StoredCharacterAsync();
        character.Quests.Start(5, s_now);
        await SaveAsync(character);

        character.Quests.Complete(5, s_now);
        var snapshot = CharacterSaveSnapshot.Take(character);
        await _saves.WriteAsync([snapshot.Batch]);
        await _saves.WriteAsync([snapshot.Batch]);   // a retried save writes it again

        QuestLog loaded = await ReloadAsync();
        Assert.Empty(loaded.Active);
        Assert.Equal([5u], loaded.Completed);
        Assert.Equal(s_now, loaded.CompletedAt(5));
    }

    [Fact]
    public async Task Take_a_characters_quest_rows_with_it_when_the_character_is_deleted()
    {
        CharacterEntity character = await StoredCharacterAsync();
        character.Quests.SetProgress(character.Quests.Start(5, s_now), 51, 2);
        character.Quests.Complete(4, s_now);
        await SaveAsync(character);

        await using (CharacterDbContext context = _database.CreateDbContext())
            await context.Characters.Where(c => c.Id == character.Data!.Id).ExecuteDeleteAsync();

        await using CharacterDbContext read = _database.CreateDbContext();
        Assert.Empty(read.CharacterQuests);
        Assert.Empty(read.CharacterQuestObjectives);
        Assert.Empty(read.CharacterCompletedQuests);
    }
}
