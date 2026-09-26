using Avalon.Database;
using Avalon.Database.Character;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.Server.World.UnitTests.Handlers;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Entities;
using Avalon.World.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Server.World.UnitTests.Pvp;

/// <summary>The PvP flag and its off timer survive a save through the real save path on SQLite (#164).</summary>
public sealed class PvpSaveRoundTripShould : IDisposable
{
    private static readonly DateTime OffAt = new(2026, 9, 26, 12, 5, 0, DateTimeKind.Utc);

    private readonly SqliteDatabase<CharacterDbContext> _database = SqliteDatabase.Characters();
    private readonly CharacterSaveRepository _saves;

    public PvpSaveRoundTripShould()
    {
        using (CharacterDbContext context = _database.CreateDbContext())
            context.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");

        _saves = new CharacterSaveRepository(new DbTransactionRunner<CharacterDbContext>(_database));
    }

    public void Dispose() => _database.Dispose();

    /// <summary>A character whose row is already in the database, loaded as select would.</summary>
    private async Task<CharacterEntity> LoadedAsync()
    {
        CharacterEntity character = TestCharacters.New(id: 1);
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

    [Fact]
    public async Task Keep_the_flag_and_the_timer_across_a_save()
    {
        CharacterEntity character = await LoadedAsync();
        character.Data!.PvpEnabled = true;
        character.Data.PvpOffAt = OffAt;
        character.MarkPvpChanged();

        await SaveAsync(character);

        await using CharacterDbContext context = _database.CreateDbContext();
        Character row = await context.Characters.AsNoTracking().SingleAsync(c => c.Id == character.Data.Id);
        Assert.True(row.PvpEnabled);
        Assert.Equal(OffAt, row.PvpOffAt);
    }
}
