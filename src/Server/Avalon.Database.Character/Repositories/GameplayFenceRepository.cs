using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Avalon.Database.Character.Repositories;

public sealed class GameplayWriteRejectedException() : DbUpdateException("The gameplay writer no longer holds this account's durable save authority.");

/// <summary>
/// An account's durable save guard in one world's characters database. The database's clock decides how long a guard
/// holds (#1009): the lease end a caller asks for is a cap, and the guard holds the earlier of it and
/// <see cref="GameAuthPolicy.SessionLeaseLifetime" /> past the database's own time. Advance, activate and renew answer
/// the end the guard holds, or null when refused, so a caller on another clock hands on that end, never a later one.
/// </summary>
public interface IGameplayFenceRepository
{
    Task<DateTime?> AdvanceAsync(GameplayWriteAuthority authority, bool blocked, DateTime leaseUntil, CancellationToken cancellationToken);
    Task<DateTime?> ActivateAsync(GameplayWriteAuthority authority, DateTime leaseUntil, CancellationToken cancellationToken);
    Task<DateTime?> RenewAsync(GameplayWriteAuthority authority, DateTime leaseUntil, CancellationToken cancellationToken);
    Task<bool> EndAsync(GameplayWriteAuthority authority, CancellationToken cancellationToken);
    Task<AccountGameplayFence?> FindAsync(AccountId accountId, CancellationToken cancellationToken);
}

public sealed class GameplayFenceRepository(IDbContextFactory<CharacterDbContext> factory, TimeProvider? clock = null) : IGameplayFenceRepository
{
    public async Task<AccountGameplayFence?> FindAsync(AccountId accountId, CancellationToken cancellationToken)
    {
        await using CharacterDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.AccountGameplayFences.AsNoTracking().SingleOrDefaultAsync(f => f.AccountId == accountId, cancellationToken);
    }
    public async Task<DateTime?> AdvanceAsync(GameplayWriteAuthority authority, bool blocked, DateTime leaseUntil, CancellationToken cancellationToken)
    {
        if (!Valid(authority)) return null;
        await using CharacterDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        AccountGameplayFence guard = await LockAsync(db, authority.AccountId, cancellationToken);
        DateTime now = await NowAsync(db, clock, cancellationToken);
        DateTime until = Held(leaseUntil, now);
        if (until <= now || guard.ConsolidationId is not null || guard.FencingToken > authority.FencingToken) return null;
        if (guard.FencingToken == authority.FencingToken)
        {
            return guard.GameSessionId == authority.GameSessionId &&
                (blocked ? guard.Mode == GameplayFenceMode.Blocked : guard.Mode is GameplayFenceMode.Pending or GameplayFenceMode.Active)
                ? until : null;
        }

        guard.GameSessionId = authority.GameSessionId;
        guard.FencingToken = authority.FencingToken;
        guard.Mode = blocked ? GameplayFenceMode.Blocked : GameplayFenceMode.Pending;
        guard.LeaseUntil = until;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return until;
    }
    public Task<DateTime?> ActivateAsync(GameplayWriteAuthority authority, DateTime leaseUntil, CancellationToken cancellationToken) =>
        SetLease(authority, leaseUntil, activate: true, cancellationToken);
    public Task<DateTime?> RenewAsync(GameplayWriteAuthority authority, DateTime leaseUntil, CancellationToken cancellationToken) =>
        SetLease(authority, leaseUntil, activate: false, cancellationToken);
    private async Task<DateTime?> SetLease(GameplayWriteAuthority authority, DateTime leaseUntil, bool activate, CancellationToken cancellationToken)
    {
        if (!Valid(authority)) return null;
        await using CharacterDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        AccountGameplayFence guard = await LockAsync(db, authority.AccountId, cancellationToken);
        DateTime now = await NowAsync(db, clock, cancellationToken);
        DateTime until = Held(leaseUntil, now);
        if (guard.GameSessionId != authority.GameSessionId || guard.FencingToken != authority.FencingToken ||
            guard.ConsolidationId is not null || guard.LeaseUntil <= now || until <= now ||
            !(guard.Mode == GameplayFenceMode.Active || (activate && guard.Mode == GameplayFenceMode.Pending)))
        {
            return null;
        }

        guard.Mode = GameplayFenceMode.Active;
        guard.LeaseUntil = until;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return until;
    }
    /// <summary>
    /// The end a guard holds for the end asked: never more than the lease lifetime past the database's own time. The end
    /// asked was worked out on the caller's clock, so refusing one past that lifetime refused every lease a caller whose
    /// clock ran ahead of the database's asked for (#1009); capping it holds the guard to the same lifetime.
    /// </summary>
    private static DateTime Held(DateTime leaseUntil, DateTime now)
    {
        DateTime longest = now.Add(GameAuthPolicy.SessionLeaseLifetime);
        return leaseUntil < longest ? leaseUntil : longest;
    }
    public async Task<bool> EndAsync(GameplayWriteAuthority authority, CancellationToken cancellationToken)
    {
        if (!Valid(authority)) return false;
        await using CharacterDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        AccountGameplayFence guard = await LockAsync(db, authority.AccountId, cancellationToken);
        if (guard.GameSessionId != authority.GameSessionId || guard.FencingToken != authority.FencingToken) return false;
        guard.Mode = GameplayFenceMode.Blocked;
        guard.LeaseUntil = await NowAsync(db, clock, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
    internal static bool Valid(GameplayWriteAuthority authority) => authority.AccountId.Value > 0 && authority.GameSessionId != Guid.Empty && authority.FencingToken > 0;
    internal static async Task<AccountGameplayFence> LockAsync(CharacterDbContext db, AccountId accountId, CancellationToken cancellationToken)
    {
        // A neutral guard also serializes first saves/first barrier insertion; no missing-row lock is assumed.
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"AccountGameplayFences\" (\"AccountId\", \"GameSessionId\", \"FencingToken\", \"Mode\", \"LeaseUntil\", \"ConsolidationId\") VALUES ({accountId.Value}, {Guid.Empty}, {0L}, {(int)GameplayFenceMode.Blocked}, {DateTime.UnixEpoch}, NULL) ON CONFLICT (\"AccountId\") DO NOTHING", cancellationToken);
        await db.AccountGameplayFences.Where(f => f.AccountId == accountId)
            .ExecuteUpdateAsync(u => u.SetProperty(f => f.FencingToken, f => f.FencingToken), cancellationToken);
        return await db.AccountGameplayFences.SingleAsync(f => f.AccountId == accountId, cancellationToken);
    }
    internal static Task<DateTime> NowAsync(CharacterDbContext db, TimeProvider? clock, CancellationToken cancellationToken) => db.Database.IsNpgsql()
        ? db.Database.SqlQueryRaw<DateTime>("SELECT clock_timestamp() AS \"Value\"").SingleAsync(cancellationToken)
        : Task.FromResult((clock ?? TimeProvider.System).GetUtcNow().UtcDateTime);
}
