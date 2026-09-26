using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Auth.Repositories;

public interface IPersonalAccessTokenRepository : IRepository<PersonalAccessToken, PersonalAccessTokenId>
{
    Task<PersonalAccessToken?> FindByHashAsync(byte[] hash, CancellationToken cancellationToken = default);
    Task<List<PersonalAccessToken>> ListByAccountAsync(AccountId accountId, bool includeRevoked, CancellationToken cancellationToken = default);
    Task<int> RevokeAllForAccountAsync(AccountId accountId, AccountId revokedBy, CancellationToken cancellationToken = default);
    Task<bool> UpdateLastUsedIfStaleAsync(PersonalAccessTokenId id, DateTime now, TimeSpan minStale, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts <paramref name="token"/>, but only while the credentials of
    /// <paramref name="reauthenticatedAccount"/> (the account whose password authorised the mint)
    /// have not changed after <paramref name="reauthenticatedAt"/> (#495), in one transaction that
    /// holds that account's row, so a concurrent change either refuses this insert or revokes it.
    /// Returns <c>null</c>, inserting nothing, when they changed.
    /// </summary>
    Task<PersonalAccessToken?> CreateUnlessCredentialsChangedAsync(PersonalAccessToken token,
        AccountId reauthenticatedAccount, DateTime reauthenticatedAt, CancellationToken cancellationToken = default);
}

public class PersonalAccessTokenRepository(IDbContextFactory<AuthDbContext> contextFactory)
    : EntityFrameworkRepository<PersonalAccessToken, PersonalAccessTokenId, AuthDbContext>(contextFactory),
        IPersonalAccessTokenRepository
{
    public async Task<PersonalAccessToken?> FindByHashAsync(byte[] hash, CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);

        return await context.PersonalAccessTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TokenHash == hash, cancellationToken);
    }

    public async Task<List<PersonalAccessToken>> ListByAccountAsync(AccountId accountId, bool includeRevoked, CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);

        var query = context.PersonalAccessTokens
            .AsNoTracking()
            .Where(p => p.AccountId == accountId);

        if (!includeRevoked)
        {
            query = query.Where(p => p.RevokedAt == null);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<int> RevokeAllForAccountAsync(AccountId accountId, AccountId revokedBy, CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);

        return await RevokeAllForAccountAsync(context, accountId, revokedBy, DateTime.UtcNow, cancellationToken);
    }

    /// <summary>
    /// Revokes on a context the caller owns, so the statement joins that context's transaction.
    /// Same statement as the instance overload — one definition, two lifetimes.
    /// </summary>
    public static Task<int> RevokeAllForAccountAsync(AuthDbContext context, AccountId accountId, AccountId revokedBy,
        DateTime now, CancellationToken cancellationToken = default)
    {
        return context.PersonalAccessTokens
            .Where(p => p.AccountId == accountId && p.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.RevokedAt, now)
                    .SetProperty(p => p.RevokedBy, revokedBy),
                cancellationToken);
    }

    public async Task<PersonalAccessToken?> CreateUnlessCredentialsChangedAsync(PersonalAccessToken token,
        AccountId reauthenticatedAccount, DateTime reauthenticatedAt, CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);
        // An uncommitted transaction rolls back when it is disposed, so the refusal needs no catch.
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // First: the account row is held until the insert commits, so a credentials change
        // either committed before this (and refuses it here) or waits for it, and its revocation
        // then takes this token.
        if (!await AccountRepository.HoldCredentialsUnchangedSinceAsync(context, reauthenticatedAccount,
                reauthenticatedAt, cancellationToken))
            return null;

        var entry = context.TrackForInsert(token);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return entry.Entity;
    }

    public async Task<bool> UpdateLastUsedIfStaleAsync(PersonalAccessTokenId id, DateTime now, TimeSpan minStale, CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);

        var threshold = now - minStale;
        var rows = await context.PersonalAccessTokens
            .Where(p => p.Id == id && (p.LastUsedAt == null || p.LastUsedAt < threshold))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.LastUsedAt, now), cancellationToken);
        return rows > 0;
    }
}
