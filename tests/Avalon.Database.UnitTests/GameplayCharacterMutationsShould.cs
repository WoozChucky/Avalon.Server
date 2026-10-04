using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Database.Character;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Microsoft.EntityFrameworkCore;
using Xunit;
using CharacterRow = Avalon.Domain.Characters.Character;

namespace Avalon.Database.UnitTests;

public sealed class GameplayCharacterMutationsShould
{
    private static async Task<GameplayWriteAuthority> Admit(SqliteDatabase<CharacterDbContext> database, long account, long fence = 1)
    {
        var writer = new GameplayWriteAuthority(new AccountId(account), Guid.NewGuid(), fence);
        var guards = new GameplayFenceRepository(database); var until = DateTime.UtcNow.AddSeconds(44);
        Assert.True(await guards.AdvanceAsync(writer, false, until, CancellationToken.None));
        Assert.True(await guards.ActivateAsync(writer, until, CancellationToken.None)); return writer;
    }
    private static CharacterCreationBatch Batch(long account, string name) => new(
        new CharacterRow { AccountId = new AccountId(account), Name = name, CreationDate = DateTime.UtcNow },
        new CharacterStats(), [new CharacterAbility { AbilityId = new AbilityId(1) }], [], []);

    [Fact]
    public async Task Create_all_initial_rows_in_one_authorized_transaction_and_enforce_count_under_the_guard()
    {
        using var database = SqliteDatabase.Characters(); var authority = await Admit(database, 42); var repository = new CharacterRepository(database);
        var created = await repository.CreateForGameplayAsync(authority, Batch(42, "Testplayer"), 1);
        Assert.Null(created.Error); Assert.NotNull(created.Character);
        await using var db = database.CreateDbContext();
        Assert.Equal(1, await db.Characters.CountAsync()); Assert.Equal(created.Character.Id, (await db.CharacterStats.SingleAsync()).CharacterId);
        Assert.Equal(created.Character.Id, (await db.CharacterAbilities.SingleAsync()).CharacterId);
        Assert.Equal("MAX_CHARACTERS", (await repository.CreateForGameplayAsync(authority, Batch(42, "Secondplayer"), 1)).Error);
        Assert.Equal(1, await db.Characters.CountAsync());
    }
    [Fact]
    public async Task Roll_back_the_root_when_a_child_insert_fails()
    {
        using var database = SqliteDatabase.Characters(); var authority = await Admit(database, 42); var repository = new CharacterRepository(database);
        var batch = Batch(42, "Rollbackboy") with { Abilities = [new CharacterAbility { AbilityId = new AbilityId(1) }, new CharacterAbility { AbilityId = new AbilityId(1) }] };
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.CreateForGameplayAsync(authority, batch, 5));
        await using var db = database.CreateDbContext(); Assert.Empty(await db.Characters.ToListAsync()); Assert.Empty(await db.CharacterStats.ToListAsync());
    }
    [Fact]
    public async Task Old_selection_and_delete_cannot_follow_a_character_to_its_new_owner()
    {
        using var database = SqliteDatabase.Characters(); var source = await Admit(database, 42); var repository = new CharacterRepository(database);
        var character = (await repository.CreateForGameplayAsync(source, Batch(42, "Transferee"), 5)).Character!;
        var consolidation = new CharacterConsolidationRepository(database); var operation = Guid.NewGuid(); var target = new AccountId(43);
        Assert.True(await consolidation.PrepareAsync(operation, source.AccountId, target, CancellationToken.None));
        await Assert.ThrowsAsync<GameplayWriteRejectedException>(() => repository.UpdateForGameplayAsync(source, character));
        await Assert.ThrowsAsync<GameplayWriteRejectedException>(() => repository.DeleteForGameplayAsync(source, character.Id));
        Assert.True(await new GameplayFenceRepository(database).EndAsync(source, CancellationToken.None));
        Assert.Null((await consolidation.TransferAsync(operation, source.AccountId, target, CancellationToken.None)).Error);
        Assert.True(await consolidation.ReleaseTargetAsync(operation, source.AccountId, target, CancellationToken.None));
        var survivor = await Admit(database, 43, 2);
        await Assert.ThrowsAsync<GameplayWriteRejectedException>(() => repository.FindForGameplayAsync(source, character.Id));
        await Assert.ThrowsAsync<GameplayWriteRejectedException>(() => repository.DeleteForGameplayAsync(source, character.Id));
        await Assert.ThrowsAsync<GameplayWriteRejectedException>(() => repository.UpdateForGameplayAsync(source, character));
        Assert.Equal(target, (await repository.FindForGameplayAsync(survivor, character.Id))!.AccountId);
    }
}
