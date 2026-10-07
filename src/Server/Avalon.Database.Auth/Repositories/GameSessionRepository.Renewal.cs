using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Avalon.Database.Auth.Repositories;

public sealed partial class GameSessionRepository
{
    public async Task<bool> TryRenewAsync(AccountId accountId, Guid sessionId, long fence, string serverId,
        int credentialsVersion, long sessionEpoch, DateTime now, DateTime leaseUntil, DateTime licenseUntil,
        CancellationToken cancellationToken = default)
    {
        now = clock?.GetUtcNow().UtcDateTime ?? now;
        if (sessionId == Guid.Empty || fence <= 0 || leaseUntil <= now || leaseUntil > now.Add(GameAuthPolicy.SessionLeaseLifetime) ||
            licenseUntil < leaseUntil || licenseUntil > now.AddMinutes(5))
        {
            return false;
        }

        await using AuthDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await AccountRepository.HoldGameAuthorityAsync(db, accountId, credentialsVersion, sessionEpoch, now, cancellationToken)) return false;
        // End and other head writes may hold this row independently of the account lock.
        // Acquire it before taking the time used by the admission predicate.
        await db.GameSessions.Where(h => h.AccountId == accountId)
            .ExecuteUpdateAsync(u => u.SetProperty(h => h.FencingToken, h => h.FencingToken), cancellationToken);
        now = clock?.GetUtcNow().UtcDateTime ?? now;
        if (leaseUntil <= now || licenseUntil <= now) return false;
        int changed = await db.GameSessions.Where(h => h.AccountId == accountId && h.GameSessionId == sessionId &&
                h.FencingToken == fence && h.ServerId == serverId && h.CredentialsVersion == credentialsVersion &&
                h.SessionEpoch == sessionEpoch && h.State == GameSessionState.Active && h.LeaseUntil > now)
            .ExecuteUpdateAsync(u => u.SetProperty(h => h.LeaseUntil, leaseUntil).SetProperty(h => h.LicenseUntil, licenseUntil), cancellationToken);
        if (leaseUntil <= (clock?.GetUtcNow().UtcDateTime ?? now)) return false;
        await transaction.CommitAsync(cancellationToken);
        return changed == 1;
    }
}
