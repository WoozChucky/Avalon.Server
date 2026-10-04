using System.Globalization;
using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Auth.Repositories;

public sealed record AccountConsolidationRequest(Guid OperationId, AccountId TargetAccountId, string SteamSubject,
    int CredentialsVersion, long SessionEpoch, Guid? ConfirmedMfaId, IReadOnlyList<WorldId> Worlds, DateTime ProofExpiresAt);
public sealed record AccountConsolidationResult(string? Error, AccountConsolidation? Operation = null);

public interface IAccountConsolidationRepository
{
    Task<AccountConsolidationResult> BeginAsync(AccountConsolidationRequest request, CancellationToken cancellationToken);
    Task<AccountConsolidation?> FindPendingForTargetAsync(AccountId target, CancellationToken cancellationToken);
    Task<AccountConsolidation?> FindAsync(Guid operationId, CancellationToken cancellationToken);
    Task<bool> RecordTransferAsync(Guid operationId, WorldId world, int count, CancellationToken cancellationToken);
    Task<bool> FinalizeAsync(Guid operationId, CancellationToken cancellationToken);
    Task<bool> RecordGuardReleasedAsync(Guid operationId, WorldId world, CancellationToken cancellationToken);
    Task<bool> CompleteAsync(Guid operationId, CancellationToken cancellationToken);
}

public sealed class AccountConsolidationRepository(IDbContextFactory<AuthDbContext> factory, TimeProvider clock) : IAccountConsolidationRepository
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    public async Task<AccountConsolidation?> FindPendingForTargetAsync(AccountId target, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.AccountConsolidations.AsNoTracking().Include(o => o.Worlds).Where(o => o.TargetAccountId == target && o.State != AccountConsolidationState.Completed)
            .OrderByDescending(o => o.AuthorizedAt).FirstOrDefaultAsync(cancellationToken);
    }
    public async Task<AccountConsolidation?> FindAsync(Guid operationId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.AccountConsolidations.AsNoTracking().Include(o => o.Worlds).SingleOrDefaultAsync(o => o.Id == operationId, cancellationToken);
    }
    public async Task<AccountConsolidationResult> BeginAsync(AccountConsolidationRequest request, CancellationToken cancellationToken)
    {
        if (request.OperationId == Guid.Empty || request.TargetAccountId.Value <= 0 || request.Worlds.Count is < 1 or > 1024 ||
            request.Worlds.Any(w => w.Value == 0) || request.Worlds.Select(w => w.Value).Distinct().Count() != request.Worlds.Count ||
            !ulong.TryParse(request.SteamSubject, NumberStyles.None, CultureInfo.InvariantCulture, out var subject) ||
            subject == 0 || subject.ToString(CultureInfo.InvariantCulture) != request.SteamSubject)
            return new("INVALID_CONSOLIDATION");
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var existing = await db.AccountConsolidations.AsNoTracking().Include(o => o.Worlds).SingleOrDefaultAsync(o => o.Id == request.OperationId, cancellationToken);
        if (existing is not null) return Matches(existing, request) ? new(null, existing) : new("INVALID_CONSOLIDATION");
        if (request.ProofExpiresAt.Kind != DateTimeKind.Utc || request.ProofExpiresAt <= Now || request.ProofExpiresAt > Now.AddMinutes(5)) return new("PROOF_EXPIRED");
        var identity = await db.ExternalIdentities.AsNoTracking().SingleOrDefaultAsync(i => i.Provider == "steam" && i.ProviderSubject == request.SteamSubject, cancellationToken);
        if (identity is null || identity.AccountId == request.TargetAccountId) return new("CONSOLIDATION_NOT_REQUIRED");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await LockRoots(db, identity.AccountId, request.TargetAccountId, cancellationToken)) return new("ACCOUNT_UNAVAILABLE");
        // An exact competing retry may have committed while this request waited on the roots.
        existing = await db.AccountConsolidations.AsNoTracking().Include(o => o.Worlds).SingleOrDefaultAsync(o => o.Id == request.OperationId, cancellationToken);
        if (existing is not null) return Matches(existing, request) ? new(null, existing) : new("INVALID_CONSOLIDATION");
        var source = await db.Accounts.AsNoTracking().SingleAsync(a => a.Id == identity.AccountId, cancellationToken);
        var target = await db.Accounts.AsNoTracking().SingleAsync(a => a.Id == request.TargetAccountId, cancellationToken);
        if (!SourceEligible(source) || !Eligible(target) || source.GameplayConsolidationId is not null || target.GameplayConsolidationId is not null ||
            target.CredentialsVersion != request.CredentialsVersion || target.SessionEpoch != request.SessionEpoch ||
            source.CredentialsVersion == int.MaxValue || source.SessionEpoch >= long.MaxValue - 1 || target.SessionEpoch >= long.MaxValue - 1)
            return new("ACCOUNT_UNAVAILABLE");
        var mfa = await db.MfaSetups.Where(m => m.AccountId == target.Id && m.Status == MfaSetupStatus.Confirmed).Select(m => (Guid?)m.Id).SingleOrDefaultAsync(cancellationToken);
        if (mfa != request.ConfirmedMfaId) return new("AUTHORITY_CHANGED");
        var links = await db.ExternalIdentities.AsNoTracking().Where(i => i.AccountId == source.Id || i.AccountId == target.Id).ToListAsync(cancellationToken);
        if (links.Count(i => i.AccountId == source.Id) != 1 || !links.Any(i => i.Id == identity.Id && i.AccountId == source.Id && i.ProviderSubject == request.SteamSubject) ||
            links.Any(i => i.AccountId == target.Id && i.Provider == "steam")) return new("IDENTITY_CONFLICT");
        if (request.ProofExpiresAt <= Now) return new("PROOF_EXPIRED");
        var operation = new AccountConsolidation { Id = request.OperationId, SourceAccountId = source.Id, TargetAccountId = target.Id,
            SteamSubject = request.SteamSubject, TargetCredentialsVersion = request.CredentialsVersion, TargetSessionEpoch = request.SessionEpoch,
            ConfirmedMfaId = request.ConfirmedMfaId, AuthorizedAt = Now,
            Worlds = request.Worlds.OrderBy(w => w.Value).Select(w => new AccountConsolidationWorld { ConsolidationId = request.OperationId, WorldId = w.Value }).ToList() };
        db.AccountConsolidations.Add(operation);
        await db.Accounts.Where(a => a.Id == source.Id || a.Id == target.Id).ExecuteUpdateAsync(u =>
            u.SetProperty(a => a.GameplayConsolidationId, (Guid?)operation.Id).SetProperty(a => a.SessionEpoch, a => a.SessionEpoch + 1), cancellationToken);
        await db.RefreshTokens.Where(t => t.AccountId == source.Id || (t.AccountId == target.Id && t.Client == SessionClient.Launcher))
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.Revoked, true), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (request.ProofExpiresAt <= Now) return new("PROOF_EXPIRED");
        await transaction.CommitAsync(cancellationToken);
        return new(null, operation);
    }
    public async Task<bool> RecordTransferAsync(Guid operationId, WorldId world, int count, CancellationToken cancellationToken)
    {
        if (count < 0) return false;
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.AccountConsolidationWorlds.Where(w => w.ConsolidationId == operationId && w.WorldId == world.Value && w.TransferredAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(w => w.TransferredAt, (DateTime?)Now).SetProperty(w => w.TransferredCharacters, count), cancellationToken) == 1 ||
            await db.AccountConsolidationWorlds.AnyAsync(w => w.ConsolidationId == operationId && w.WorldId == world.Value && w.TransferredAt != null && w.TransferredCharacters == count, cancellationToken);
    }
    public async Task<bool> FinalizeAsync(Guid operationId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await LockOperation(db, operationId, cancellationToken)) return false;
        var operation = await db.AccountConsolidations.Include(o => o.Worlds).SingleAsync(o => o.Id == operationId, cancellationToken);
        if (operation.State is AccountConsolidationState.Finalized or AccountConsolidationState.Completed) return true;
        if (operation.Worlds.Count == 0 || operation.Worlds.Any(w => w.TransferredAt is null) ||
            !await LockRoots(db, operation.SourceAccountId, operation.TargetAccountId, cancellationToken)) return false;
        var source = await db.Accounts.AsNoTracking().SingleAsync(a => a.Id == operation.SourceAccountId, cancellationToken);
        var target = await db.Accounts.AsNoTracking().SingleAsync(a => a.Id == operation.TargetAccountId, cancellationToken);
        if (!SourceEligible(source) || !Eligible(target) || source.GameplayConsolidationId != operationId || target.GameplayConsolidationId != operationId ||
            source.CredentialsVersion == int.MaxValue || source.SessionEpoch == long.MaxValue || target.SessionEpoch == long.MaxValue) return false;
        var identity = await db.ExternalIdentities.SingleOrDefaultAsync(i => i.AccountId == source.Id && i.Provider == "steam" && i.ProviderSubject == operation.SteamSubject, cancellationToken);
        if (identity is null || await db.ExternalIdentities.AnyAsync(i => (i.AccountId == target.Id && i.Provider == "steam") || (i.AccountId == source.Id && i.Id != identity.Id), cancellationToken)) return false;
        identity.AccountId = target.Id;
        await db.Accounts.Where(a => a.Id == source.Id).ExecuteUpdateAsync(u => u.SetProperty(a => a.Status, AccountStatus.Deactivated)
            .SetProperty(a => a.SessionEpoch, a => a.SessionEpoch + 1).SetProperty(a => a.CredentialsVersion, a => a.CredentialsVersion + 1)
            .SetProperty(a => a.Online, false).SetProperty(a => a.OnlineSessionId, (Guid?)null), cancellationToken);
        await db.Accounts.Where(a => a.Id == target.Id).ExecuteUpdateAsync(u => u.SetProperty(a => a.SessionEpoch, a => a.SessionEpoch + 1)
            .SetProperty(a => a.GameplayConsolidationId, (Guid?)null).SetProperty(a => a.Online, false).SetProperty(a => a.OnlineSessionId, (Guid?)null), cancellationToken);
        operation.State = AccountConsolidationState.Finalized;
        operation.FinalizedAt = Now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
    public async Task<bool> RecordGuardReleasedAsync(Guid operationId, WorldId world, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.AccountConsolidationWorlds.Where(w => w.ConsolidationId == operationId && w.WorldId == world.Value && w.TransferredAt != null)
            .ExecuteUpdateAsync(u => u.SetProperty(w => w.GuardReleasedAt, w => w.GuardReleasedAt ?? Now), cancellationToken) == 1;
    }
    public async Task<bool> CompleteAsync(Guid operationId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.AccountConsolidations.Where(o => o.Id == operationId && o.State == AccountConsolidationState.Finalized &&
                !o.Worlds.Any(w => w.GuardReleasedAt == null))
            .ExecuteUpdateAsync(u => u.SetProperty(o => o.State, AccountConsolidationState.Completed).SetProperty(o => o.CompletedAt, (DateTime?)Now), cancellationToken) == 1 ||
            await db.AccountConsolidations.AnyAsync(o => o.Id == operationId && o.State == AccountConsolidationState.Completed, cancellationToken);
    }
    private bool Eligible(Account account) => account.Status == AccountStatus.Active && (account.AccessLevel & AccountAccessLevel.Player) != 0 && !account.IsLockedAt(Now);
    private bool SourceEligible(Account account) => Eligible(account) && account.AccessLevel == AccountAccessLevel.Player && account.IsStoreGenerated;
    private static bool Matches(AccountConsolidation operation, AccountConsolidationRequest request) => operation.TargetAccountId == request.TargetAccountId &&
        operation.SteamSubject == request.SteamSubject && operation.TargetCredentialsVersion == request.CredentialsVersion &&
        operation.TargetSessionEpoch == request.SessionEpoch && operation.ConfirmedMfaId == request.ConfirmedMfaId &&
        operation.Worlds.Select(w => w.WorldId).Order().SequenceEqual(request.Worlds.Select(w => w.Value).Order());
    private static async Task<bool> LockRoots(AuthDbContext db, AccountId source, AccountId target, CancellationToken cancellationToken)
    {
        foreach (var id in new[] { source, target }.OrderBy(a => a.Value))
            if (await db.Accounts.Where(a => a.Id == id).ExecuteUpdateAsync(u => u.SetProperty(a => a.SessionEpoch, a => a.SessionEpoch), cancellationToken) != 1) return false;
        return true;
    }
    private static async Task<bool> LockOperation(AuthDbContext db, Guid id, CancellationToken cancellationToken) =>
        await db.AccountConsolidations.Where(o => o.Id == id).ExecuteUpdateAsync(u => u.SetProperty(o => o.State, o => o.State), cancellationToken) == 1;
}
