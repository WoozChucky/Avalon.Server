using Avalon.Common;
using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.Character;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Auth;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
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
        Assert.NotNull(await fences.AdvanceAsync(old, false, _clock.GetUtcNow().UtcDateTime.AddSeconds(45), CancellationToken.None));
        Assert.NotNull(await fences.ActivateAsync(old, _clock.GetUtcNow().UtcDateTime.AddSeconds(45), CancellationToken.None));
        var saves = new CharacterSaveRepository(new DbTransactionRunner<CharacterDbContext>(database), _clock);
        row.Gold = 25;
        await saves.WriteAsync([Batch(row, old)]);
        Assert.NotNull(await fences.AdvanceAsync(replacement, true, _clock.GetUtcNow().UtcDateTime.AddSeconds(45), CancellationToken.None));
        row.Gold = 999;
        var staleItem = new ItemInstance { Id = new ItemInstanceId(Guid.NewGuid()), TemplateId = new ItemTemplateId(1), CharacterId = row.Id, Count = 99, ItemLevel = 1, UpdatedAt = _clock.GetUtcNow().UtcDateTime };
        var stale = new CharacterSaveBatch(row, [staleItem], [], [new CharacterInventory { CharacterId = row.Id, Container = InventoryType.Bag, Slot = 0, ItemId = staleItem.Id }], []) { Authority = old };
        await Assert.ThrowsAsync<GameplayWriteRejectedException>(() => saves.WriteAsync([stale]));
        await using CharacterDbContext read = database.CreateDbContext();
        Assert.Equal(25UL, (await read.Characters.AsNoTracking().SingleAsync()).Gold);
        Assert.Equal(2, (await read.AccountGameplayFences.AsNoTracking().SingleAsync()).FencingToken);
        Assert.Empty(await read.ItemInstances.AsNoTracking().ToListAsync());
        Assert.Empty(await read.CharacterInventory.AsNoTracking().ToListAsync());
    }
    /// <summary>
    /// #1013: a confirmed takeover replaces a pending session, as it does an active one. The replaced admission, its
    /// first activation having reached the guard and its answer lost, can neither activate in the Auth database nor
    /// advance, activate or write through the guard once the takeover advanced it. A reconnect never replaces it.
    /// </summary>
    [Fact]
    public async Task Fence_out_a_pending_admission_that_a_confirmed_takeover_replaced()
    {
        using var auth = SqliteDatabase.Auth();
        using var database = SqliteDatabase.Characters();
        Account account = await new AccountRepository(auth).CreateAsync(StoreAuthenticationModelShould.Account("TAKEOVER"));
        var sessions = new GameSessionRepository(auth, _clock);
        var fences = new GameplayFenceRepository(database, _clock);
        var saves = new CharacterSaveRepository(new DbTransactionRunner<CharacterDbContext>(database), _clock);
        CharacterRow row = await Seed(database, account.Id, 1);
        DateTime now = _clock.GetUtcNow().UtcDateTime, until = now.AddSeconds(45);
        GameSession pending = (await sessions.TryReserveAsync(StoreAuthenticationModelShould.Reservation(account.Id, 0), now))!;
        var replaced = new GameplayWriteAuthority(account.Id, pending.GameSessionId, pending.FencingToken);
        Assert.NotNull(await fences.AdvanceAsync(replaced, false, until, CancellationToken.None));

        Assert.Null(await sessions.TryReserveAsync(StoreAuthenticationModelShould.Reservation(account.Id, 1), now));
        Assert.Null(await sessions.TryReserveAsync(StoreAuthenticationModelShould.Reservation(account.Id, 1) with { Takeover = true, Reconnect = true }, now));
        GameSession? takeover = await sessions.TryReserveAsync(StoreAuthenticationModelShould.Reservation(account.Id, 1) with { Takeover = true }, now);
        Assert.NotNull(takeover);
        Assert.Equal(2, takeover!.FencingToken);
        Assert.False(await sessions.TryActivateAsync(account.Id, pending.GameSessionId, pending.FencingToken, now, until));
        Assert.NotNull(await fences.AdvanceAsync(new(account.Id, takeover.GameSessionId, takeover.FencingToken), false, until, CancellationToken.None));

        Assert.Null(await fences.AdvanceAsync(replaced, false, until, CancellationToken.None));
        Assert.Null(await fences.ActivateAsync(replaced, until, CancellationToken.None));
        row.Gold = 999;
        await Assert.ThrowsAsync<GameplayWriteRejectedException>(() => saves.WriteAsync([Batch(row, replaced)]));
        await using CharacterDbContext read = database.CreateDbContext();
        Assert.Equal(0UL, (await read.Characters.AsNoTracking().SingleAsync()).Gold);
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
        Assert.NotNull(await fences.AdvanceAsync(authority, false, until, CancellationToken.None));
        await Assert.ThrowsAsync<GameplayWriteRejectedException>(() => saves.WriteAsync([Batch(row, authority)]));
        Assert.Null(await fences.AdvanceAsync(new(authority.AccountId, authority.GameSessionId, 2), false, until, CancellationToken.None));
        Assert.Null(await fences.ActivateAsync(new(authority.AccountId, Guid.NewGuid(), 3), until, CancellationToken.None));
        Assert.NotNull(await fences.ActivateAsync(authority, until, CancellationToken.None));
        await Assert.ThrowsAsync<GameplayWriteRejectedException>(() => saves.WriteAsync([new CharacterSaveBatch(row, [], [], [], [])]));
        await Assert.ThrowsAsync<GameplayWriteRejectedException>(() => saves.WriteAsync([Batch(row, new(new AccountId(8), authority.GameSessionId, 3))]));
        _clock.Advance(TimeSpan.FromSeconds(45));
        await Assert.ThrowsAsync<GameplayWriteRejectedException>(() => saves.WriteAsync([Batch(row, authority)]));
        Assert.Null(await fences.RenewAsync(authority, until.AddSeconds(45), CancellationToken.None));
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
            Assert.NotNull(await fences.AdvanceAsync(authority, false, until, CancellationToken.None));
            Assert.NotNull(await fences.ActivateAsync(authority, until, CancellationToken.None));
        }
        Assert.NotNull(await fences.AdvanceAsync(new(b.AccountId, Guid.NewGuid(), 2), true, until, CancellationToken.None));
        first.Gold = 10; second.Gold = 20;
        var saves = new CharacterSaveRepository(new DbTransactionRunner<CharacterDbContext>(database), _clock);
        await Assert.ThrowsAsync<GameplayWriteRejectedException>(() => saves.WriteAsync([Batch(second, b), Batch(first, a)]));
        await using CharacterDbContext read = database.CreateDbContext();
        Assert.All(await read.Characters.AsNoTracking().ToListAsync(), row => Assert.Equal(0UL, row.Gold));
    }
    private static CharacterSaveBatch Batch(CharacterRow row, GameplayWriteAuthority authority) => new(row, [], [], [], []) { Authority = authority };
    private static async Task<CharacterRow> Seed(SqliteDatabase<CharacterDbContext> database, AccountId account, uint id)
    {
        var row = new CharacterRow { Id = new CharacterId(id), AccountId = account, Name = "Fence" + (char)('a' + id), CreationDate = DateTime.UtcNow };
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
