using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Auth.Repositories;

public interface IRefreshTokenRepository
{
    Task<RefreshToken> CreateAsync(RefreshToken token, CancellationToken cancellationToken = default);
    Task<RefreshToken?> FindByHashAsync(byte[] hash, CancellationToken cancellationToken = default);
    Task UpdateAsync(RefreshToken token, CancellationToken cancellationToken = default);
    Task<int> RevokeFamilyAsync(Guid familyId, CancellationToken cancellationToken = default);
    Task<int> RevokeAllForAccountAsync(AccountId accountId, CancellationToken cancellationToken = default);
}

public sealed class RefreshTokenRepository(IDbContextFactory<AuthDbContext> contextFactory) : IRefreshTokenRepository
{
    public async Task<RefreshToken> CreateAsync(RefreshToken token, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var entry = context.TrackForInsert(token);
        await context.SaveChangesAsync(cancellationToken);
        return entry.Entity;
    }

    public async Task<RefreshToken?> FindByHashAsync(byte[] hash, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.RefreshTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Hash == hash, cancellationToken);
    }

    public async Task UpdateAsync(RefreshToken token, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        context.TrackForUpdate(token);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> RevokeFamilyAsync(Guid familyId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.RefreshTokens
            .Where(t => t.FamilyId == familyId && !t.Revoked)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Revoked, true), cancellationToken);
    }

    public async Task<int> RevokeAllForAccountAsync(AccountId accountId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await RevokeAllForAccountAsync(context, accountId, cancellationToken);
    }

    /// <summary>
    /// Revokes on a context the caller owns, so the statement joins that context's transaction.
    /// Same statement as the instance overload — one definition, two lifetimes.
    /// </summary>
    public static Task<int> RevokeAllForAccountAsync(AuthDbContext context, AccountId accountId,
        CancellationToken cancellationToken = default)
    {
        return context.RefreshTokens
            .Where(t => t.AccountId == accountId && !t.Revoked)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Revoked, true), cancellationToken);
    }
}
