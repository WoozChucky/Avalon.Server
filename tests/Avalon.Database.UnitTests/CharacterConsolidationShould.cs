using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Database.Character;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;
using Xunit;
using CharacterRow = Avalon.Domain.Characters.Character;

namespace Avalon.Database.UnitTests;

public sealed class CharacterConsolidationShould
{
    private readonly AccountId _source = new(7), _target = new(8);
    private readonly Guid _operation = Guid.NewGuid();
    private readonly ManualClock _clock = new(DateTimeOffset.UtcNow);
    private async Task Seed(SqliteDatabase<CharacterDbContext> database)
    {
        await using CharacterDbContext db = database.CreateDbContext();
        for (uint id = 1; id <= 13; id++)
        {
            db.Characters.Add(new CharacterRow
        {
            Id = new CharacterId(id),
            AccountId = id <= 8 ? _source : _target,
            Name = "Transfer" + id,
            CreationDate = _clock.GetUtcNow().UtcDateTime,
            Money = id
        });
        }

        db.ItemInstances.Add(new ItemInstance
        {
            Id = new ItemInstanceId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            CharacterId = new CharacterId(1),
            TemplateId = new ItemTemplateId(1),
            Count = 3,
            UpdatedAt = _clock.GetUtcNow().UtcDateTime
        });
        db.CharacterQuests.Add(new CharacterQuest { CharacterId = new CharacterId(1), QuestId = 42, Stage = 2, AcceptedAt = _clock.GetUtcNow().UtcDateTime });
        await db.SaveChangesAsync();
    }
    [Fact]
    public async Task Move_all_characters_without_changing_ids_children_or_discarding_characters_above_the_creation_cap()
    {
        using var database = SqliteDatabase.Characters();
        await Seed(database);
        var repository = new CharacterConsolidationRepository(database, _clock);
        Assert.True(await repository.PrepareAsync(_operation, _source, _target, CancellationToken.None));
        CharacterConsolidationResult result = await repository.TransferAsync(_operation, _source, _target, CancellationToken.None);
        Assert.Null(result.Error);
        Assert.Equal(8, result.TransferredCharacters);
        CharacterConsolidationResult repeated = await repository.TransferAsync(_operation, _source, _target, CancellationToken.None);
        Assert.Equal(result, repeated);
        await using CharacterDbContext db = database.CreateDbContext();
        Assert.Equal(13, await db.Characters.CountAsync(c => c.AccountId == _target));
        Assert.Equal(Enumerable.Range(1, 13).Select(i => (uint)i), await db.Characters.OrderBy(c => c.Id).Select(c => c.Id.Value).ToArrayAsync());
        Assert.Equal(new CharacterId(1), (await db.ItemInstances.SingleAsync()).CharacterId);
        Assert.Equal(3u, (await db.ItemInstances.SingleAsync()).Count);
        Assert.Equal(2, (await db.CharacterQuests.SingleAsync()).Stage);
        Assert.Equal(_operation, (await db.AccountGameplayFences.SingleAsync(f => f.AccountId == _source)).ConsolidationId);
        Assert.True(await repository.ReleaseTargetAsync(_operation, _source, _target, CancellationToken.None));
        Assert.Null((await new GameplayFenceRepository(database, _clock).FindAsync(_target, CancellationToken.None))!.ConsolidationId);
        Assert.NotNull((await new GameplayFenceRepository(database, _clock).FindAsync(_source, CancellationToken.None))!.ConsolidationId);
    }
    [Fact]
    public async Task Drain_the_original_admitted_writer_and_reject_its_snapshot_after_transfer()
    {
        using var database = SqliteDatabase.Characters();
        await Seed(database);
        var old = new GameplayWriteAuthority(_source, Guid.NewGuid(), 1);
        var fences = new GameplayFenceRepository(database, _clock);
        DateTime until = _clock.GetUtcNow().UtcDateTime.AddSeconds(45);
        Assert.True(await fences.AdvanceAsync(old, false, until, CancellationToken.None));
        Assert.True(await fences.ActivateAsync(old, until, CancellationToken.None));
        var repository = new CharacterConsolidationRepository(database, _clock);
        Assert.True(await repository.PrepareAsync(_operation, _source, _target, CancellationToken.None));
        Assert.Equal("WAITING_FOR_SESSION", (await repository.TransferAsync(_operation, _source, _target, CancellationToken.None)).Error);
        CharacterRow row = (await new CharacterRepository(database).FindByIdAsync(new CharacterId(1)))!;
        row.Money = 77;
        var saves = new CharacterSaveRepository(new DbTransactionRunner<CharacterDbContext>(database), _clock);
        var batch = new CharacterSaveBatch(row, [], [], [], []) { Authority = old };
        await saves.WriteAsync([batch]);
        Assert.True(await fences.EndAsync(old, CancellationToken.None));
        Assert.Null((await repository.TransferAsync(_operation, _source, _target, CancellationToken.None)).Error);
        await Assert.ThrowsAsync<GameplayWriteRejectedException>(() => saves.WriteAsync([batch]));
        Assert.Equal(77UL, (await new CharacterRepository(database).FindByIdAsync(row.Id))!.Money);
    }
    [Fact]
    public async Task Block_unfenced_character_mutations_during_consolidation_and_stale_ownership_updates_after_transfer()
    {
        using var database = SqliteDatabase.Characters();
        await Seed(database);
        var characters = new CharacterRepository(database);
        CharacterRow old = (await characters.FindByIdAsync(new CharacterId(1)))!;
        var repository = new CharacterConsolidationRepository(database, _clock);
        Assert.True(await repository.PrepareAsync(_operation, _source, _target, CancellationToken.None));
        await Assert.ThrowsAsync<GameplayWriteRejectedException>(() => characters.DeleteAsync(old.Id));
        await Assert.ThrowsAsync<GameplayWriteRejectedException>(() => characters.TryRenameAsync(old.Id, "FrozenRename"));
        await Assert.ThrowsAsync<GameplayWriteRejectedException>(() => characters.UpdateAsync(old));
        await Assert.ThrowsAsync<GameplayWriteRejectedException>(() => characters.CreateAsync(new CharacterRow { AccountId = _source, Name = "FrozenCreate", CreationDate = _clock.GetUtcNow().UtcDateTime }));
        await using (CharacterDbContext db = database.CreateDbContext())
        {
            ItemInstance item = await db.ItemInstances.SingleAsync();
            item.Count = 999;
            await Assert.ThrowsAsync<GameplayWriteRejectedException>(() => db.SaveChangesAsync());
        }
        Assert.Null((await repository.TransferAsync(_operation, _source, _target, CancellationToken.None)).Error);
        Assert.True(await repository.ReleaseTargetAsync(_operation, _source, _target, CancellationToken.None));
        old.Money = 999;
        await Assert.ThrowsAsync<GameplayWriteRejectedException>(() => characters.UpdateAsync(old));
        Assert.Equal(_target, (await characters.FindByIdAsync(old.Id))!.AccountId);
    }
    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
