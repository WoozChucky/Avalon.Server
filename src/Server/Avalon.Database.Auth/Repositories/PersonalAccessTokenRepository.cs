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
