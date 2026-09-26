using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Auth.Repositories;

/// <summary>How a <see cref="IRefreshTokenRepository.RotateAsync"/> ended.</summary>
public enum RefreshRotation
{
    /// <summary>The parent was revoked by this call and the child inserted.</summary>
    Rotated,

    /// <summary>The account's credentials version is no longer the parent's (#495): nothing written.</summary>
    CredentialsChanged,

    /// <summary>
    /// The parent was no longer live when this call went to revoke it: another rotation, or a
    /// revocation, got there first (#495). Nothing written.
    /// </summary>
    ParentNotLive,
}

public interface IRefreshTokenRepository
{
    /// <summary>
    /// Replaces <paramref name="parent"/> with <paramref name="child"/> in one transaction (#495):
    /// refused when the account's credentials version is no longer the parent's; otherwise the
    /// parent is revoked by a conditional write, and the child is inserted only when that write is
    /// the one that revoked it, so of two rotations of one token exactly one goes on. The account
    /// row is held first, so a concurrent credentials change either refuses this rotation or
    /// revokes the child it inserted.
    /// </summary>
    Task<RefreshRotation> RotateAsync(RefreshToken parent, RefreshToken child, DateTime now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a family's first token, in one transaction that holds the account row, but only
    /// while the account's credentials version is still <see cref="RefreshToken.CredentialsVersion"/>
    /// (#495): a login that proved the old password cannot open a family after the change
    /// committed. Returns <c>false</c>, inserting nothing, when the version moved.
    /// </summary>
    Task<bool> CreateIfCredentialsCurrentAsync(RefreshToken token, CancellationToken cancellationToken = default);

    Task<RefreshToken> CreateAsync(RefreshToken token, CancellationToken cancellationToken = default);
    Task<RefreshToken?> FindByHashAsync(byte[] hash, CancellationToken cancellationToken = default);

    /// <summary>The token a rotation of the family's token at <paramref name="index"/> inserted, if any.</summary>
    Task<RefreshToken?> FindChildAsync(Guid familyId, uint index, CancellationToken cancellationToken = default);
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

    public async Task<bool> CreateIfCredentialsCurrentAsync(RefreshToken token,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        if (!await AccountRepository.HoldCredentialsVersionAsync(context, token.AccountId, token.CredentialsVersion,
                cancellationToken))
            return false;

        context.TrackForInsert(token);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<RefreshRotation> RotateAsync(RefreshToken parent, RefreshToken child, DateTime now,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        // An uncommitted transaction rolls back when it is disposed, so the refusals need no catch.
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        if (!await AccountRepository.HoldCredentialsVersionAsync(context, parent.AccountId, parent.CredentialsVersion,
                cancellationToken))
            return RefreshRotation.CredentialsChanged;

        // The read that found the parent live proves nothing by the time this runs: only the
        // caller whose write flips it from live to revoked goes on. Another rotation of the same
        // token, or a revocation, that got there first leaves this one nothing to revoke.
        int revoked = await context.RefreshTokens
            .Where(t => t.Id == parent.Id && !t.Revoked)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Revoked, true)
                .SetProperty(t => t.Usages, t => t.Usages + 1), cancellationToken);
        if (revoked == 0)
            return RefreshRotation.ParentNotLive;

        context.TrackForInsert(child);
        await context.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return RefreshRotation.Rotated;
    }

    public async Task<RefreshToken?> FindChildAsync(Guid familyId, uint index, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.RefreshTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.FamilyId == familyId && t.Index == index + 1, cancellationToken);
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
