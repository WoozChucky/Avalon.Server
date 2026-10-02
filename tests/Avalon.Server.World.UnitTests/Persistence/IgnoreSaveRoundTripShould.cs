using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Character;
using Avalon.Database.Character.Repositories;
using Avalon.Server.World.UnitTests.Handlers;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Entities;
using Avalon.World.Persistence;
using Avalon.World.Social;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Avalon.Server.World.UnitTests.Persistence;

/// <summary>
/// The ignore list (#723) through the real save path on SQLite and back through the select-time load, with both
/// foreign keys cascading: an entry added, one removed, one naming a character deleted before or after the save.
/// </summary>
public sealed class IgnoreSaveRoundTripShould : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly SqliteDatabase<CharacterDbContext> _database = SqliteDatabase.Characters();
    private readonly CharacterSaveRepository _saves;
    private readonly CharacterIgnoreRepository _ignores;

    public IgnoreSaveRoundTripShould()
    {
        using (CharacterDbContext context = _database.CreateDbContext())
            context.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");
        _saves = new CharacterSaveRepository(new DbTransactionRunner<CharacterDbContext>(_database));
        _ignores = new CharacterIgnoreRepository(_database);
    }

    public void Dispose() => _database.Dispose();

    private async Task<CharacterEntity> StoredCharacterAsync(uint id, string name)
    {
        CharacterEntity character = TestCharacters.New(id);
        character.Data!.Name = name;
        character.Name = name;
        await using CharacterDbContext context = _database.CreateDbContext();
        context.Characters.Add(character.Data!);
        await context.SaveChangesAsync();
        return character;
    }

    private async Task SaveAsync(CharacterEntity character)
    {
        CharacterSaveSnapshot snapshot = CharacterSaveSnapshot.Take(character);
        await _saves.WriteAsync([snapshot.Batch]);
        character.SaveState.Acknowledge(snapshot.Marks);
    }

    private async Task<IgnoreList> ReloadAsync(uint owner)
    {
        var list = new IgnoreList(new SaveStateTracker());
        list.Load(await _ignores.GetByCharacterIdAsync(owner));
        return list;
    }

    private async Task DeleteCharacterAsync(uint id)
    {
        await using CharacterDbContext context = _database.CreateDbContext();
        CharacterId target = id;
        await context.Characters.Where(c => c.Id == target).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Round_trip_the_list_in_order_with_names_and_times()
    {
        CharacterEntity owner = await StoredCharacterAsync(1, "Aren");
        await StoredCharacterAsync(2, "Kaela");
        await StoredCharacterAsync(3, "Tom");
        owner.Ignores.Add(3, "Tom", Now);
        owner.Ignores.Add(2, "Kaela", Now.AddMinutes(1));

        await SaveAsync(owner);
        IgnoreList loaded = await ReloadAsync(1);

        Assert.Equal([new IgnoredCharacter(3, "Tom", Now), new IgnoredCharacter(2, "Kaela", Now.AddMinutes(1))], loaded.Entries);
        Assert.False(owner.SaveState.HasChanges);
    }

    [Fact]
    public async Task Delete_an_entry_taken_off_the_list()
    {
        CharacterEntity owner = await StoredCharacterAsync(1, "Aren");
        await StoredCharacterAsync(2, "Kaela");
        owner.Ignores.Add(2, "Kaela", Now);
        await SaveAsync(owner);

        owner.Ignores.Remove(2);
        await SaveAsync(owner);

        Assert.Empty((await ReloadAsync(1)).Entries);
    }

    [Fact]
    public async Task Write_a_retried_save_once()
    {
        CharacterEntity owner = await StoredCharacterAsync(1, "Aren");
        await StoredCharacterAsync(2, "Kaela");
        owner.Ignores.Add(2, "Kaela", Now);

        CharacterSaveSnapshot snapshot = CharacterSaveSnapshot.Take(owner);
        await _saves.WriteAsync([snapshot.Batch]);
        await _saves.WriteAsync([snapshot.Batch]);

        Assert.Single((await ReloadAsync(1)).Entries);
    }

    [Fact]
    public async Task Leave_out_a_character_deleted_before_the_save_and_still_save_the_rest()
    {
        CharacterEntity owner = await StoredCharacterAsync(1, "Aren");
        await StoredCharacterAsync(2, "Kaela");
        await StoredCharacterAsync(3, "Tom");
        owner.Ignores.Add(2, "Kaela", Now);
        owner.Ignores.Add(3, "Tom", Now);
        await DeleteCharacterAsync(2);

        await SaveAsync(owner);

        Assert.Equal([3u], (await ReloadAsync(1)).Entries.Select(e => e.Id));
        Assert.False(owner.SaveState.HasChanges);
    }

    [Fact]
    public async Task Drop_a_deleted_character_from_every_list_that_names_it()
    {
        CharacterEntity owner = await StoredCharacterAsync(1, "Aren");
        await StoredCharacterAsync(2, "Kaela");
        owner.Ignores.Add(2, "Kaela", Now);
        await SaveAsync(owner);

        await DeleteCharacterAsync(2);

        Assert.Empty((await ReloadAsync(1)).Entries);
    }

    [Fact]
    public async Task Take_a_deleted_characters_own_list_with_it()
    {
        CharacterEntity owner = await StoredCharacterAsync(1, "Aren");
        await StoredCharacterAsync(2, "Kaela");
        owner.Ignores.Add(2, "Kaela", Now);
        await SaveAsync(owner);

        await DeleteCharacterAsync(1);

        await using CharacterDbContext read = _database.CreateDbContext();
        Assert.Empty(read.CharacterIgnores);
    }

    [Fact]
    public async Task Load_the_name_the_ignored_character_has_now()
    {
        CharacterEntity owner = await StoredCharacterAsync(1, "Aren");
        await StoredCharacterAsync(2, "Kaela");
        owner.Ignores.Add(2, "Kaela", Now);
        await SaveAsync(owner);

        await using (CharacterDbContext context = _database.CreateDbContext())
        {
            CharacterId renamed = 2u;
            await context.Characters.Where(c => c.Id == renamed)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.Name, "Kaelin").SetProperty(c => c.NameKey, "KAELIN"));
        }

        Assert.Equal("Kaelin", Assert.Single((await ReloadAsync(1)).Entries).Name);
    }

    [Theory]
    [InlineData("Kaela")]
    [InlineData("kAELA")]
    [InlineData("  kaela  ")]
    public async Task Find_a_character_by_name_ignoring_case_and_spaces(string name)
    {
        await StoredCharacterAsync(2, "Kaela");

        CharacterNameMatch? match = await _ignores.FindCharacterByNameAsync(name);

        Assert.Equal(new CharacterNameMatch(2u, "Kaela"), match);
        Assert.Null(await _ignores.FindCharacterByNameAsync("Nobody"));
        Assert.Null(await _ignores.FindCharacterByNameAsync("   "));
    }
}
