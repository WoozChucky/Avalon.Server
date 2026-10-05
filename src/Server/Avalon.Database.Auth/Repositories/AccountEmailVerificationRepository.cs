using Avalon.Common.ValueObjects;
using Avalon.Common.Accounts;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Auth.Repositories;

public enum EmailVerificationIssueResult { Issued, AlreadyVerified, Cooldown, AccountChanged }

public interface IAccountEmailVerificationRepository
{
    Task<EmailVerificationIssueResult> IssueAsync(AccountId accountId, string normalizedEmail, int credentialsVersion,
        string tokenHash, DateTime issuedAt, DateTime expiresAt, TimeSpan cooldown, CancellationToken ct);
    Task<bool> ConsumeAsync(AccountId accountId, string tokenHash, DateTime now, CancellationToken ct);
    Task<bool> InvalidateAsync(AccountId accountId, string tokenHash, CancellationToken ct);
    Task<AccountEmailVerification?> FindAsync(AccountId accountId, CancellationToken ct);
}

public sealed class AccountEmailVerificationRepository(IDbContextFactory<AuthDbContext> factory) : IAccountEmailVerificationRepository
{
    public async Task<EmailVerificationIssueResult> IssueAsync(AccountId accountId, string normalizedEmail, int credentialsVersion,
        string tokenHash, DateTime issuedAt, DateTime expiresAt, TimeSpan cooldown, CancellationToken ct)
    {
        if (expiresAt <= issuedAt || cooldown <= TimeSpan.Zero || tokenHash.Length != 64)
            throw new ArgumentException("Invalid verification challenge interval or digest.");
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (!await HoldAccount(db, accountId, ct)) return EmailVerificationIssueResult.AccountChanged;
        var account = await db.Accounts.AsNoTracking().SingleAsync(a => a.Id == accountId, ct);
        if (!Eligible(account, issuedAt) || account.Email != normalizedEmail || account.CredentialsVersion != credentialsVersion)
            return EmailVerificationIssueResult.AccountChanged;
        if (account.EmailVerifiedAt is not null) return EmailVerificationIssueResult.AlreadyVerified;
        var challenge = await db.AccountEmailVerifications.SingleOrDefaultAsync(x => x.AccountId == accountId, ct);
        if (challenge is not null && issuedAt < challenge.IssuedAt + cooldown) return EmailVerificationIssueResult.Cooldown;
        if (challenge is null)
        {
            challenge = new AccountEmailVerification { AccountId = accountId, Email = normalizedEmail, TokenHash = tokenHash };
            db.AccountEmailVerifications.Add(challenge);
        }
        challenge.Email = normalizedEmail; challenge.TokenHash = tokenHash; challenge.CredentialsVersion = credentialsVersion;
        challenge.IssuedAt = issuedAt; challenge.ExpiresAt = expiresAt; challenge.ConsumedAt = null; challenge.InvalidatedAt = null;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return EmailVerificationIssueResult.Issued;
    }

    public async Task<bool> ConsumeAsync(AccountId accountId, string tokenHash, DateTime now, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (!await HoldAccount(db, accountId, ct)) return false;
        var account = await db.Accounts.AsNoTracking().SingleAsync(a => a.Id == accountId, ct);
        var challenge = await db.AccountEmailVerifications.SingleOrDefaultAsync(x => x.AccountId == accountId, ct);
        if (challenge is null || challenge.TokenHash != tokenHash || challenge.ConsumedAt is not null || challenge.InvalidatedAt is not null
            || challenge.IssuedAt > now || challenge.ExpiresAt <= now || !Eligible(account, now) || account.EmailVerifiedAt is not null
            || challenge.Email != account.Email || challenge.CredentialsVersion != account.CredentialsVersion) return false;
        await db.Accounts.Where(a => a.Id == accountId).ExecuteUpdateAsync(u => u.SetProperty(a => a.EmailVerifiedAt, (DateTime?)now), ct);
        challenge.ConsumedAt = now;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<bool> InvalidateAsync(AccountId accountId, string tokenHash, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (!await HoldAccount(db, accountId, ct)) return false;
        // The challenge's timestamp suffices as an invalidation marker; never change a consumed proof.
        var count = await db.AccountEmailVerifications.Where(x => x.AccountId == accountId && x.TokenHash == tokenHash
                && x.ConsumedAt == null && x.InvalidatedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.InvalidatedAt, x => (DateTime?)x.IssuedAt), ct);
        await transaction.CommitAsync(ct);
        return count == 1;
    }

    public async Task<AccountEmailVerification?> FindAsync(AccountId accountId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.AccountEmailVerifications.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == accountId, ct);
    }

    private static bool Eligible(Account account, DateTime now) => account.Status == AccountStatus.Active
        && (account.AccessLevel & AccountAccessLevel.Player) != 0 && account.GameplayConsolidationId is null && !account.IsLockedAt(now);

    // Same lock order for issue, consume, cleanup and account edits, including cross-process callers.
    private static async Task<bool> HoldAccount(AuthDbContext db, AccountId id, CancellationToken ct) =>
        await db.Accounts.Where(a => a.Id == id).ExecuteUpdateAsync(u => u.SetProperty(a => a.SessionEpoch, a => a.SessionEpoch), ct) == 1;
}
