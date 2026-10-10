using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Avalon.Database.Auth.Repositories;

public sealed record GameSessionReservation(AccountId AccountId, long ExpectedFence, Guid GameSessionId,
    string ServerId, ushort WorldId, string Environment, int CredentialsVersion, long SessionEpoch,
    DateTime LicenseUntil, bool Takeover)
{
    public Guid GameContextId { get; init; }
    public DateTime AdmissionExpiresAt { get; init; } = DateTime.MaxValue;
}

public interface IGameSessionRepository
{
    Task<GameSession?> FindAsync(AccountId accountId, CancellationToken cancellationToken = default);
    Task<GameSession?> TryReserveAsync(GameSessionReservation reservation, DateTime now, CancellationToken cancellationToken = default);
    Task<bool> TryActivateAsync(AccountId accountId, Guid sessionId, long fence, DateTime now, DateTime leaseUntil,
        CancellationToken cancellationToken = default);
    Task<bool> TryRenewAsync(AccountId accountId, Guid sessionId, long fence, string serverId,
        int credentialsVersion, long sessionEpoch, DateTime now, DateTime leaseUntil, DateTime licenseUntil,
        CancellationToken cancellationToken = default);
    Task<bool> TryEndAsync(AccountId accountId, Guid sessionId, long fence, DateTime now, CancellationToken cancellationToken = default);
}

public sealed partial class GameSessionRepository(IDbContextFactory<AuthDbContext> factory, TimeProvider? clock = null) : IGameSessionRepository
{
    public async Task<GameSession?> FindAsync(AccountId accountId, CancellationToken cancellationToken = default)
    {
        await using AuthDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.GameSessions.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == accountId, cancellationToken);
    }

    public async Task<GameSession?> TryReserveAsync(GameSessionReservation reservation, DateTime now,
        CancellationToken cancellationToken = default)
    {
        now = clock?.GetUtcNow().UtcDateTime ?? now;
        if (reservation.AdmissionExpiresAt <= now || reservation.ExpectedFence < 0 || reservation.ExpectedFence == long.MaxValue ||
            reservation.GameSessionId == Guid.Empty || string.IsNullOrWhiteSpace(reservation.ServerId) ||
            reservation.ServerId.Length > 128 || reservation.WorldId == 0 ||
            reservation.LicenseUntil <= now || reservation.LicenseUntil > now.AddMinutes(5) ||
            (reservation.Environment != "production" && reservation.Environment != "development"))
        {
            return null;
        }

        await using AuthDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Lock the account before looking at the head, including on the first insert. Revocation and two
        // first reservations therefore serialize on a row that already exists, rather than on a missing head.
        if (!await AccountRepository.HoldGameAuthorityAsync(db, reservation.AccountId, reservation.CredentialsVersion,
                reservation.SessionEpoch, cancellationToken))
        {
            return null;
        }

        now = clock?.GetUtcNow().UtcDateTime ?? now;
        if (reservation.AdmissionExpiresAt <= now || reservation.LicenseUntil <= now) return null;
        GameSession? head = await db.GameSessions.SingleOrDefaultAsync(x => x.AccountId == reservation.AccountId, cancellationToken);
        // Recover only the originally committed reservation, even when the caller lost its SQL response.
        if (head is not null && head.GameSessionId == reservation.GameSessionId &&
            head.GameContextId == reservation.GameContextId && head.FencingToken == reservation.ExpectedFence + 1 &&
            head.ServerId == reservation.ServerId && head.WorldId == reservation.WorldId && head.Environment == reservation.Environment &&
            head.CredentialsVersion == reservation.CredentialsVersion && head.SessionEpoch == reservation.SessionEpoch &&
            head.State != GameSessionState.Ended && head.LeaseUntil > now && head.LicenseUntil > now)
        {
            return head;
        }

        if ((head?.FencingToken ?? 0) != reservation.ExpectedFence ||
            (head?.State == GameSessionState.Pending && head.LeaseUntil > now) ||
            (head?.State == GameSessionState.Active && head.LeaseUntil > now && !reservation.Takeover))
        {
            return null;
        }

        GameSession next = NewHead(reservation, now, head);
        if (head is null) db.GameSessions.Add(next);
        else db.Entry(head).CurrentValues.SetValues(next);
        await db.SaveChangesAsync(cancellationToken);
        now = clock?.GetUtcNow().UtcDateTime ?? now;
        if (reservation.AdmissionExpiresAt <= now || reservation.LicenseUntil <= now) return null;
        await transaction.CommitAsync(cancellationToken);
        return next;
    }

    private static GameSession NewHead(GameSessionReservation reservation, DateTime now, GameSession? previous) => new()
    {
        AccountId = reservation.AccountId,
        GameSessionId = reservation.GameSessionId,
        GameContextId = reservation.GameContextId,
        FencingToken = checked(reservation.ExpectedFence + 1),
        ServerId = reservation.ServerId,
        WorldId = reservation.WorldId,
        Environment = reservation.Environment,
        State = GameSessionState.Pending,
        CredentialsVersion = reservation.CredentialsVersion,
        SessionEpoch = reservation.SessionEpoch,
        CreatedAt = now,
        LeaseUntil = Earlier(now.Add(GameAuthPolicy.SessionLeaseLifetime), reservation.LicenseUntil),
        LicenseUntil = reservation.LicenseUntil,
        // An expired pending target never became the writer. Retain the last active ancestor for its save barrier.
        PreviousGameSessionId = previous?.State == GameSessionState.Pending ? previous.PreviousGameSessionId : previous?.GameSessionId,
        PreviousServerId = previous?.State == GameSessionState.Pending ? previous.PreviousServerId : previous?.ServerId,
        PreviousWorldId = previous?.State == GameSessionState.Pending ? previous.PreviousWorldId : previous?.WorldId,
    };

    public async Task<bool> TryActivateAsync(AccountId accountId, Guid sessionId, long fence, DateTime now, DateTime leaseUntil,
        CancellationToken cancellationToken = default)
    {
        await using AuthDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        // Read only to select the expected versions; the lock below refuses a changed account, and the
        // conditional session write refuses any superseding reservation. Fence barriers run before this call.
        GameSession? head = await db.GameSessions.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == accountId, cancellationToken);
        if (head is null || leaseUntil <= now || leaseUntil > now.Add(GameAuthPolicy.SessionLeaseLifetime) || leaseUntil > head.LicenseUntil) return false;
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await AccountRepository.HoldGameAuthorityAsync(db, accountId, head.CredentialsVersion, head.SessionEpoch, cancellationToken))
            return false;
        await db.GameSessions.Where(h => h.AccountId == accountId)
            .ExecuteUpdateAsync(u => u.SetProperty(h => h.FencingToken, h => h.FencingToken), cancellationToken);
        now = clock?.GetUtcNow().UtcDateTime ?? now;
        if (leaseUntil <= now) return false;
        int changed = await db.GameSessions.Where(x => x.AccountId == accountId && x.GameSessionId == sessionId &&
            x.FencingToken == fence && x.State == GameSessionState.Pending && x.LeaseUntil > now && x.LicenseUntil >= leaseUntil)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.State, GameSessionState.Active)
                .SetProperty(x => x.LeaseUntil, leaseUntil), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return changed == 1;
    }

    public async Task<bool> TryEndAsync(AccountId accountId, Guid sessionId, long fence, DateTime now,
        CancellationToken cancellationToken = default)
    {
        await using AuthDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.GameSessions.Where(x => x.AccountId == accountId && x.GameSessionId == sessionId && x.FencingToken == fence)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.State, GameSessionState.Ended)
                .SetProperty(x => x.LeaseUntil, now), cancellationToken) == 1;
    }

    private static DateTime Earlier(DateTime first, DateTime second) => first < second ? first : second;
}
