using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Character;
using Avalon.Database.Character.Repositories;
using Avalon.Server.World.UnitTests.Handlers;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Entities;
using Avalon.World.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Server.World.UnitTests.Persistence;

/// <summary>
/// A character's talent nodes (#1181) through the real save path on SQLite, foreign keys on, and back through the
/// select-time read: allocated, dropped, written twice, beside another character, and gone with the character.
/// </summary>
public sealed class TalentSaveRoundTripShould : IDisposable
{
    private readonly SqliteDatabase<CharacterDbContext> _database = SqliteDatabase.Characters();
    private readonly CharacterSaveRepository _saves;
    private readonly CharacterTalentRepository _talents;

    public TalentSaveRoundTripShould()
    {
        using (CharacterDbContext context = _database.CreateDbContext())
            context.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");
        _saves = new CharacterSaveRepository(new DbTransactionRunner<CharacterDbContext>(_database));
        _talents = new CharacterTalentRepository(_database);
    }

    public void Dispose() => _database.Dispose();

    private async Task<CharacterEntity> StoredCharacterAsync(uint id)
    {
        CharacterEntity character = TestCharacters.New(id);
        await using CharacterDbContext context = _database.CreateDbContext();
        context.Characters.Add(character.Data!);
        await context.SaveChangesAsync();
        await AdmittedCharacter.BindAsync(_database, character);
        return character;
    }

    private async Task<int[]> ReloadAsync(uint owner) =>
        [.. (await _talents.GetByCharacterIdAsync(owner)).Select(t => t.NodeId)];

    [Fact]
    public async Task Round_trip_allocated_and_dropped_talents_idempotently()
    {
        CharacterEntity character = await StoredCharacterAsync(1);
        CharacterEntity other = await StoredCharacterAsync(2);
        other.Talents.Allocate([3u, 9u]);
        var otherSave = CharacterSaveSnapshot.Take(other);
        await _saves.WriteAsync([otherSave.Batch]);

        character.Talents.Allocate([2u, 3u, 4u]);
        var first = CharacterSaveSnapshot.Take(character);
        await _saves.WriteAsync([first.Batch]);
        character.SaveState.Acknowledge(first.Marks);
        int[] afterFirst = await ReloadAsync(1);
        Assert.Equal([2, 3, 4], afterFirst);
        Assert.False(character.SaveState.HasChanges);

        character.Talents.Drop(3);
        character.Talents.Allocate([5u]);
        var second = CharacterSaveSnapshot.Take(character);
        await _saves.WriteAsync([second.Batch]);
        await _saves.WriteAsync([second.Batch]);
        character.SaveState.Acknowledge(second.Marks);

        int[] afterSecond = await ReloadAsync(1);
        int[] untouched = await ReloadAsync(2);
        Assert.Equal([2, 4, 5], afterSecond);
        Assert.Equal([3, 9], untouched);
        Assert.False(character.SaveState.HasChanges);

        await using (CharacterDbContext context = _database.CreateDbContext())
        {
            CharacterId deleted = 1u;
            await context.Characters.Where(c => c.Id == deleted).ExecuteDeleteAsync();
        }

        Assert.Empty(await ReloadAsync(1));
        int[] survivor = await ReloadAsync(2);
        Assert.Equal([3, 9], survivor);
    }
}
