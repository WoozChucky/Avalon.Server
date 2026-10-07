using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Database.Character;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.World.Public.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;
using CharacterRow = Avalon.Domain.Characters.Character;

namespace Avalon.Database.UnitTests;

public sealed class GameplayFenceShould
{
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero));
    [Fact]
    public async Task Reject_a_partitioned_old_writer_and_commit_no_row_or_inventory_changes_after_takeover()
    {
        using var database = SqliteDatabase.Characters();
        var account = new AccountId(7);
        var old = new GameplayWriteAuthority(account, Guid.NewGuid(), 1);
        var replacement = new GameplayWriteAuthority(account, Guid.NewGuid(), 2);
        var fences = new GameplayFenceRepository(database, _clock);
        CharacterRow row = await Seed(database, account, 1);
        Assert.True(await fences.AdvanceAsync(old, false, _clock.GetUtcNow().UtcDateTime.AddSeconds(45), CancellationToken.None));
        Assert.True(await fences.ActivateAsync(old, _clock.GetUtcNow().UtcDateTime.AddSeconds(45), CancellationToken.None));
        var saves = new CharacterSaveRepository(new DbTransactionRunner<CharacterDbContext>(database), _clock);
        row.Money = 25;
        await saves.WriteAsync([Batch(row, old)]);
        Assert.True(await fences.AdvanceAsync(replacement, true, _clock.GetUtcNow().UtcDateTime.AddSeconds(45), CancellationToken.None));
        row.Money = 999;
        var staleItem = new ItemInstance { Id = new ItemInstanceId(Guid.NewGuid()), TemplateId = new ItemTemplateId(1), CharacterId = row.Id, Count = 99, UpdatedAt = _clock.GetUtcNow().UtcDateTime };
        var stale = new CharacterSaveBatch(row, [staleItem], [], [new CharacterInventory { CharacterId = row.Id, Container = InventoryType.Bag, Slot = 0, ItemId = staleItem.Id }], []) { Authority = old };
        await Assert.ThrowsAsync<GameplayWriteRejectedException>(() => saves.WriteAsync([stale]));
        await using CharacterDbContext read = database.CreateDbContext();
        Assert.Equal(25UL, (await read.Characters.AsNoTracking().SingleAsync()).Money);
        Assert.Equal(2, (await read.AccountGameplayFences.AsNoTracking().SingleAsync()).FencingToken);
        Assert.Empty(await read.ItemInstances.AsNoTracking().ToListAsync());
        Assert.Empty(await read.CharacterInventory.AsNoTracking().ToListAsync());
    }
    [Fact]
    public async Task Refuse_pending_expired_missing_or_mismatched_authority_and_never_regress_a_barrier()
    {
        using var database = SqliteDatabase.Characters();
        var account = new AccountId(7);
        var authority = new GameplayWriteAuthority(account, Guid.NewGuid(), 3);
        CharacterRow row = await Seed(database, account, 1);
        var fences = new GameplayFenceRepository(database, _clock);
        var saves = new CharacterSaveRepository(new DbTransactionRunner<CharacterDbContext>(database), _clock);
        DateTime until = _clock.GetUtcNow().UtcDateTime.AddSeconds(45);
        Assert.True(await fences.AdvanceAsync(authority, false, until, CancellationToken.None));
        await Assert.ThrowsAsync<GameplayWriteRejectedException>(() => saves.WriteAsync([Batch(row, authority)]));
        Assert.False(await fences.AdvanceAsync(new(authority.AccountId, authority.GameSessionId, 2), false, until, CancellationToken.None));
        Assert.False(await fences.ActivateAsync(new(authority.AccountId, Guid.NewGuid(), 3), until, CancellationToken.None));
        Assert.True(await fences.ActivateAsync(authority, until, CancellationToken.None));
        await Assert.ThrowsAsync<GameplayWriteRejectedException>(() => saves.WriteAsync([new CharacterSaveBatch(row, [], [], [], [])]));
        await Assert.ThrowsAsync<GameplayWriteRejectedException>(() => saves.WriteAsync([Batch(row, new(new AccountId(8), authority.GameSessionId, 3))]));
        _clock.Advance(TimeSpan.FromSeconds(45));
        await Assert.ThrowsAsync<GameplayWriteRejectedException>(() => saves.WriteAsync([Batch(row, authority)]));
        Assert.False(await fences.RenewAsync(authority, until.AddSeconds(45), CancellationToken.None));
    }
    [Fact]
    public async Task Reject_a_multi_character_transaction_atomically_when_either_account_is_fenced()
    {
        using var database = SqliteDatabase.Characters();
        var a = new GameplayWriteAuthority(new AccountId(7), Guid.NewGuid(), 1);
        var b = new GameplayWriteAuthority(new AccountId(8), Guid.NewGuid(), 1);
        CharacterRow first = await Seed(database, a.AccountId, 1);
        CharacterRow second = await Seed(database, b.AccountId, 2);
        var fences = new GameplayFenceRepository(database, _clock);
        DateTime until = _clock.GetUtcNow().UtcDateTime.AddSeconds(45);
        foreach (GameplayWriteAuthority? authority in new[] { a, b })
        {
            Assert.True(await fences.AdvanceAsync(authority, false, until, CancellationToken.None));
            Assert.True(await fences.ActivateAsync(authority, until, CancellationToken.None));
        }
        Assert.True(await fences.AdvanceAsync(new(b.AccountId, Guid.NewGuid(), 2), true, until, CancellationToken.None));
        first.Money = 10; second.Money = 20;
        var saves = new CharacterSaveRepository(new DbTransactionRunner<CharacterDbContext>(database), _clock);
        await Assert.ThrowsAsync<GameplayWriteRejectedException>(() => saves.WriteAsync([Batch(second, b), Batch(first, a)]));
        await using CharacterDbContext read = database.CreateDbContext();
        Assert.All(await read.Characters.AsNoTracking().ToListAsync(), row => Assert.Equal(0UL, row.Money));
    }
    private static CharacterSaveBatch Batch(CharacterRow row, GameplayWriteAuthority authority) => new(row, [], [], [], []) { Authority = authority };
    private static async Task<CharacterRow> Seed(SqliteDatabase<CharacterDbContext> database, AccountId account, uint id)
    {
        var row = new CharacterRow { Id = new CharacterId(id), AccountId = account, Name = "Fence" + id, CreationDate = DateTime.UtcNow };
        await using CharacterDbContext db = database.CreateDbContext();
        db.Characters.Add(row); await db.SaveChangesAsync();
        return row;
    }
    private sealed class ManualClock(DateTimeOffset current) : TimeProvider
    {
        private DateTimeOffset _current = current;
        public override DateTimeOffset GetUtcNow() => _current;
        public void Advance(TimeSpan duration) => _current += duration;
    }
}
