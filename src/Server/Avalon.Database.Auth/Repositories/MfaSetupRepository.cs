using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Auth.Repositories;

/// <summary>How a conditional MFA write ended.</summary>
public enum MfaSetupWrite
{
    Written,

    /// <summary>The row was no longer what the caller read (confirmed, replaced or deleted meanwhile).</summary>
    Lost,

    /// <summary>
    /// The account's credentials version moved past the one the caller's session proved (#495
    /// re-review): nothing written.
    /// </summary>
    CredentialsChanged,
}

public interface IMfaSetupRepository : IRepository<MFASetup, Guid>
{
    /// <summary>The account's MFA row. There is at most one: <c>AccountId</c> is unique.</summary>
    Task<MFASetup?> FindByAccountIdAsync(AccountId accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores <paramref name="pending"/> as the account's MFA row: replaces the row in place when
    /// it is not confirmed, inserts one when there is none. Returns <c>false</c>, writing nothing,
    /// when the account's row is confirmed.
    /// </summary>
    Task<bool> UpsertPendingAsync(MFASetup pending, CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirms row <paramref name="id"/> and stores the recovery-code hashes, but only while the
    /// row is still in Setup with the secret the code was verified against. Returns <c>false</c>,
    /// writing nothing, when another request confirmed or replaced it first.
    /// <paramref name="acceptedTotpStep"/>, the step of the confirming code, is stored as the last
    /// one accepted, so that code cannot then be used to log in. The transaction first holds account
    /// <paramref name="accountId"/> at <paramref name="credentialsVersion"/>, the version the
    /// caller's session proved (#495 re-review): a password change or an MFA reset committed since
    /// refuses it.
    /// </summary>
    Task<MfaSetupWrite> TryConfirmAsync(Guid id, AccountId accountId, int credentialsVersion, byte[] verifiedSecret,
        byte[] recoveryCode1, byte[] recoveryCode2, byte[] recoveryCode3, DateTime confirmedAt, long acceptedTotpStep,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records <paramref name="step"/> as the last TOTP step accepted on confirmed row
    /// <paramref name="id"/>, but only when it is later than the one stored. Returns <c>false</c>,
    /// writing nothing, when it is not: the code was already used, or an older one.
    /// </summary>
    Task<bool> TryAcceptTotpStepAsync(Guid id, long step, CancellationToken cancellationToken = default);

    /// <summary>Deletes row <paramref name="id"/> only while it is still in Setup with <paramref name="secret"/>.</summary>
    Task DeletePendingAsync(Guid id, byte[] secret, CancellationToken cancellationToken = default);

    /// <summary>
    /// The owner's own MFA reset (#483), in one transaction: raises the account's
    /// <c>CredentialsVersion</c> (#495), deletes confirmed row
    /// <paramref name="id"/>, which spends its recovery codes, and revokes every refresh token and
    /// personal access token <paramref name="accountId"/> holds, so no session opened before the
    /// reset outlives it. Nothing is written when the account is no longer at
    /// <paramref name="credentialsVersion"/>, the version the caller's session proved
    /// (<see cref="MfaSetupWrite.CredentialsChanged"/>, #495 re-review), or when the row is no
    /// longer there and confirmed (<see cref="MfaSetupWrite.Lost"/>: a concurrent reset won).
    /// </summary>
    Task<MfaSetupWrite> ResetConfirmedAsync(Guid id, AccountId accountId, int credentialsVersion, DateTime now,
        CancellationToken cancellationToken = default);
}

public class MfaSetupRepository(IDbContextFactory<AuthDbContext> contextFactory)
    : EntityFrameworkRepository<MFASetup, Guid, AuthDbContext>(contextFactory), IMfaSetupRepository
{
    // One lost insert race is expected (two setups both found no row); more means something else.
    private const int InsertAttempts = 3;

    public async Task<MFASetup?> FindByAccountIdAsync(AccountId accountId, CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);

        // Single, not First: the unique index makes a second row impossible, and if one ever
        // exists this throws rather than let the order rows come back in decide whether MFA applies.
        return await context.MfaSetups
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.AccountId == accountId, cancellationToken);
    }

    public async Task<bool> UpsertPendingAsync(MFASetup pending, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            await using var context = await CreateContextAsync(cancellationToken);

            var replaced = await context.MfaSetups
                .Where(m => m.AccountId == pending.AccountId && m.Status != MfaSetupStatus.Confirmed)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.Secret, pending.Secret)
                    .SetProperty(m => m.RecoveryCode1, pending.RecoveryCode1)
                    .SetProperty(m => m.RecoveryCode2, pending.RecoveryCode2)
                    .SetProperty(m => m.RecoveryCode3, pending.RecoveryCode3)
                    .SetProperty(m => m.Status, pending.Status)
                    .SetProperty(m => m.CreatedAt, pending.CreatedAt)
                    .SetProperty(m => m.ConfirmedAt, pending.ConfirmedAt), cancellationToken);
            if (replaced > 0)
                return true;

            if (await context.MfaSetups.AnyAsync(
                    m => m.AccountId == pending.AccountId && m.Status == MfaSetupStatus.Confirmed, cancellationToken))
                return false;

            try
            {
                context.TrackForInsert(pending);
                await context.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateException) when (attempt < InsertAttempts)
            {
                // Another setup inserted the account's row between our update and our insert, and
                // the unique index refused ours. Go round again and replace (or defer to) that row.
            }
        }
    }

    public async Task<MfaSetupWrite> TryConfirmAsync(Guid id, AccountId accountId, int credentialsVersion,
        byte[] verifiedSecret, byte[] recoveryCode1, byte[] recoveryCode2, byte[] recoveryCode3, DateTime confirmedAt,
        long acceptedTotpStep, CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // First, as a token mint does (#495 re-review): the session's version, held for the write.
        if (!await AccountRepository.HoldCredentialsVersionAsync(context, accountId, credentialsVersion,
                cancellationToken))
            return MfaSetupWrite.CredentialsChanged;

        var confirmed = await context.MfaSetups
            .Where(m => m.Id == id && m.Status == MfaSetupStatus.Setup && m.Secret == verifiedSecret)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.RecoveryCode1, recoveryCode1)
                .SetProperty(m => m.RecoveryCode2, recoveryCode2)
                .SetProperty(m => m.RecoveryCode3, recoveryCode3)
                .SetProperty(m => m.Status, MfaSetupStatus.Confirmed)
                .SetProperty(m => m.ConfirmedAt, confirmedAt)
                .SetProperty(m => m.LastAcceptedTotpStep, acceptedTotpStep), cancellationToken);
        if (confirmed != 1)
            return MfaSetupWrite.Lost;

        await transaction.CommitAsync(cancellationToken);
        return MfaSetupWrite.Written;
    }

    public async Task<bool> TryAcceptTotpStepAsync(Guid id, long step, CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);

        var accepted = await context.MfaSetups
            .Where(m => m.Id == id && m.Status == MfaSetupStatus.Confirmed
                        && (m.LastAcceptedTotpStep == null || m.LastAcceptedTotpStep < step))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.LastAcceptedTotpStep, step), cancellationToken);

        return accepted == 1;
    }

    public async Task DeletePendingAsync(Guid id, byte[] secret, CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);

        await context.MfaSetups
            .Where(m => m.Id == id && m.Status == MfaSetupStatus.Setup && m.Secret == secret)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<MfaSetupWrite> ResetConfirmedAsync(Guid id, AccountId accountId, int credentialsVersion,
        DateTime now, CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);
        // An uncommitted transaction rolls back when it is disposed, so the throw path needs no catch.
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // First, so this transaction holds the account row before it revokes anything: a refresh
        // token issue or rotation, or a token mint, running now either sees the new version or
        // finishes before the revocations below, which then take what it issued (#495).
        // ...only while the account is still at the version the caller's session proved (#495
        // re-review): a password change committed since refuses the reset.
        if (!await AccountRepository.HoldCredentialsVersionAsync(context, accountId, credentialsVersion,
                cancellationToken))
            return MfaSetupWrite.CredentialsChanged;
        if (await AccountRepository.BumpCredentialsVersionAsync(context, accountId, cancellationToken) == 0)
            return MfaSetupWrite.Lost;

        int deleted = await context.MfaSetups
            .Where(m => m.Id == id && m.AccountId == accountId && m.Status == MfaSetupStatus.Confirmed)
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0)
            return MfaSetupWrite.Lost;

        await RefreshTokenRepository.RevokeAllForAccountAsync(context, accountId, cancellationToken);
        await PersonalAccessTokenRepository.RevokeAllForAccountAsync(context, accountId, accountId, now,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return MfaSetupWrite.Written;
    }

    /// <summary>
    /// Deletes every MFA row the account has, in any status, on a context the caller owns, so the
    /// statement joins that context's transaction. Returns the number of rows deleted.
    /// </summary>
    public static Task<int> DeleteAllForAccountAsync(AuthDbContext context, AccountId accountId,
        CancellationToken cancellationToken = default) =>
        context.MfaSetups
            .Where(m => m.AccountId == accountId)
            .ExecuteDeleteAsync(cancellationToken);
}
