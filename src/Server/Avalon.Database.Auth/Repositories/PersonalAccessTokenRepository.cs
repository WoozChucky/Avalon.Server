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

public class PersonalAccessTokenRepository : EntityFrameworkRepository<PersonalAccessToken, PersonalAccessTokenId>, IPersonalAccessTokenRepository
{
    public PersonalAccessTokenRepository(AuthDbContext db)
        : base(db)
    { }

    public Task<PersonalAccessToken?> FindByHashAsync(byte[] hash, CancellationToken cancellationToken = default)
    {
        return Context.Set<PersonalAccessToken>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TokenHash == hash, cancellationToken);
    }

    public Task<List<PersonalAccessToken>> ListByAccountAsync(AccountId accountId, bool includeRevoked, CancellationToken cancellationToken = default)
    {
        var query = Context.Set<PersonalAccessToken>()
            .AsNoTracking()
            .Where(p => p.AccountId == accountId);

        if (!includeRevoked)
        {
            query = query.Where(p => p.RevokedAt == null);
        }

        return query.ToListAsync(cancellationToken);
    }

    public async Task<int> RevokeAllForAccountAsync(AccountId accountId, AccountId revokedBy, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return await Context.Set<PersonalAccessToken>()
            .Where(p => p.AccountId == accountId && p.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.RevokedAt, now)
                    .SetProperty(p => p.RevokedBy, revokedBy),
                cancellationToken);
    }

    public async Task<bool> UpdateLastUsedIfStaleAsync(PersonalAccessTokenId id, DateTime now, TimeSpan minStale, CancellationToken cancellationToken = default)
    {
        var threshold = now - minStale;
        var rows = await Context.Set<PersonalAccessToken>()
            .Where(p => p.Id == id && (p.LastUsedAt == null || p.LastUsedAt < threshold))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.LastUsedAt, now), cancellationToken);
        return rows > 0;
    }
}
