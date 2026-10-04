using Avalon.Common.ValueObjects;
using Avalon.Common.GameAuth;
using Avalon.Domain.Characters;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Character.Repositories;

public sealed class GameplayWriteRejectedException() : DbUpdateException("The gameplay writer no longer holds this account's durable save authority.");

public interface IGameplayFenceRepository
{
    Task<bool> AdvanceAsync(GameplayWriteAuthority authority, bool blocked, DateTime leaseUntil, CancellationToken cancellationToken);
    Task<bool> ActivateAsync(GameplayWriteAuthority authority, DateTime leaseUntil, CancellationToken cancellationToken);
    Task<bool> RenewAsync(GameplayWriteAuthority authority, DateTime leaseUntil, CancellationToken cancellationToken);
    Task<bool> EndAsync(GameplayWriteAuthority authority, CancellationToken cancellationToken);
    Task<AccountGameplayFence?> FindAsync(AccountId accountId, CancellationToken cancellationToken);
}

public sealed class GameplayFenceRepository(IDbContextFactory<CharacterDbContext> factory, TimeProvider? clock = null) : IGameplayFenceRepository
{
    public async Task<AccountGameplayFence?> FindAsync(AccountId accountId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.AccountGameplayFences.AsNoTracking().SingleOrDefaultAsync(f => f.AccountId == accountId, cancellationToken);
    }
    public async Task<bool> AdvanceAsync(GameplayWriteAuthority authority, bool blocked, DateTime leaseUntil, CancellationToken cancellationToken)
    {
        if (!Valid(authority)) return false;
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var guard = await LockAsync(db, authority.AccountId, cancellationToken);
        var now = await NowAsync(db, clock, cancellationToken);
        if (leaseUntil <= now || leaseUntil > now.Add(GameAuthPolicy.SessionLeaseLifetime) || guard.ConsolidationId is not null || guard.FencingToken > authority.FencingToken) return false;
        if (guard.FencingToken == authority.FencingToken)
            return guard.GameSessionId == authority.GameSessionId && (blocked ? guard.Mode == GameplayFenceMode.Blocked : guard.Mode is GameplayFenceMode.Pending or GameplayFenceMode.Active);
        guard.GameSessionId = authority.GameSessionId;
        guard.FencingToken = authority.FencingToken;
        guard.Mode = blocked ? GameplayFenceMode.Blocked : GameplayFenceMode.Pending;
        guard.LeaseUntil = leaseUntil;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
    public Task<bool> ActivateAsync(GameplayWriteAuthority authority, DateTime leaseUntil, CancellationToken cancellationToken) =>
        SetLease(authority, leaseUntil, activate: true, cancellationToken);
    public Task<bool> RenewAsync(GameplayWriteAuthority authority, DateTime leaseUntil, CancellationToken cancellationToken) =>
        SetLease(authority, leaseUntil, activate: false, cancellationToken);
    private async Task<bool> SetLease(GameplayWriteAuthority authority, DateTime until, bool activate, CancellationToken cancellationToken)
    {
        if (!Valid(authority)) return false;
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var guard = await LockAsync(db, authority.AccountId, cancellationToken);
        var now = await NowAsync(db, clock, cancellationToken);
        if (guard.GameSessionId != authority.GameSessionId || guard.FencingToken != authority.FencingToken ||
            guard.ConsolidationId is not null || guard.LeaseUntil <= now || until <= now || until > now.Add(GameAuthPolicy.SessionLeaseLifetime) ||
            !(guard.Mode == GameplayFenceMode.Active || (activate && guard.Mode == GameplayFenceMode.Pending))) return false;
        guard.Mode = GameplayFenceMode.Active;
        guard.LeaseUntil = until;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
    public async Task<bool> EndAsync(GameplayWriteAuthority authority, CancellationToken cancellationToken)
    {
        if (!Valid(authority)) return false;
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var guard = await LockAsync(db, authority.AccountId, cancellationToken);
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
