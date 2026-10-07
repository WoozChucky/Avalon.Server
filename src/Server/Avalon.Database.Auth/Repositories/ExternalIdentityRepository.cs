using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Avalon.Database.Auth.Repositories;

public enum IdentityLinkStatus { Linked, AlreadyLinked, SubjectTaken, AccountProviderTaken, AuthorityChanged, UsernameTaken, EmailTaken, CreationRefused }
public sealed record IdentityLinkResult(IdentityLinkStatus Status, ExternalIdentity? Identity);

public sealed record IdentityLinkOperation(Guid OperationId, AccountId AccountId, string Provider, string Subject,
    int CredentialsVersion, long SessionEpoch, Guid? ConfirmedMfaId)
{
    public DateTime ProofExpiresAt { get; init; }
}

public interface IExternalIdentityRepository
{
    Task<IdentityLinkResult> CreateAccountWithStoreLinkAsync(StoreAccountCreationOperation operation, DateTime now, CancellationToken cancellationToken = default);
    Task<IdentityLinkResult> CreateAccountWithSteamLinkAsync(StoreAccountCreationOperation operation, DateTime now, CancellationToken cancellationToken = default);
    Task<IdentityLinkResult> LinkWithAuthorityAsync(IdentityLinkOperation operation, DateTime now, CancellationToken cancellationToken = default);
    Task<ExternalIdentity?> FindAsync(string provider, string subject, CancellationToken cancellationToken = default);
    Task<IdentityLinkResult> LinkAsync(AccountId accountId, string provider, string subject, DateTime now,
        CancellationToken cancellationToken = default);
}

public sealed partial class ExternalIdentityRepository(IDbContextFactory<AuthDbContext> factory, TimeProvider? clock = null) : IExternalIdentityRepository
{
    public async Task<ExternalIdentity?> FindAsync(string provider, string subject, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.ExternalIdentities.AsNoTracking().SingleOrDefaultAsync(
            x => x.Provider == provider && x.ProviderSubject == subject, cancellationToken);
    }

    public async Task<IdentityLinkResult> LinkAsync(AccountId accountId, string provider, string subject, DateTime now,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(provider) || provider.Length > 32 ||
            string.IsNullOrWhiteSpace(subject) || subject.Length > 128)
            throw new ArgumentException("Invalid provider identity shape.");
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var conflict = await FindConflictAsync(db, accountId, provider, subject, cancellationToken);
        if (conflict is not null) return conflict;
        var identity = new ExternalIdentity
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Provider = provider,
            ProviderSubject = subject,
            LinkedAt = now,
        };
        db.ExternalIdentities.Add(identity);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return new(IdentityLinkStatus.Linked, identity);
        }
        catch (DbUpdateException error) when (error.InnerException is not PostgresException pg || pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // A competing insert may have won either unique index. Query in a fresh context after the failed write.
            await using var read = await factory.CreateDbContextAsync(cancellationToken);
            var raced = await FindConflictAsync(read, accountId, provider, subject, cancellationToken);
            if (raced is null) throw;
            return raced;
        }
    }

    public async Task<IdentityLinkResult> LinkWithAuthorityAsync(IdentityLinkOperation operation, DateTime now,
        CancellationToken cancellationToken = default)
    {
        if (operation.OperationId == Guid.Empty || string.IsNullOrWhiteSpace(operation.Provider) || operation.Provider.Length > 32 ||
            operation.Provider != operation.Provider.Trim() || string.IsNullOrWhiteSpace(operation.Subject) || operation.Subject.Length > 128 || operation.Subject != operation.Subject.Trim() ||
            operation.SessionEpoch < 0 || operation.SessionEpoch == long.MaxValue)
            throw new ArgumentException("Invalid identity link operation.");
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        bool ProofExpired() => operation.ProofExpiresAt.Kind != DateTimeKind.Utc ||
            operation.ProofExpiresAt <= (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        if (ProofExpired()) return new(IdentityLinkStatus.AuthorityChanged, null);
        var current = await AccountRepository.HoldGameAuthorityAsync(db, operation.AccountId,
            operation.CredentialsVersion, operation.SessionEpoch, now, cancellationToken);
        if (ProofExpired()) return new(IdentityLinkStatus.AuthorityChanged, null);
        if (!current)
        {
            // A response can be lost after PostgreSQL commits and before Redis publishes the result.
            // Only the exact durable operation may finish that link at the epoch it advanced.
            var retry = await AccountRepository.HoldGameAuthorityAsync(db, operation.AccountId,
                operation.CredentialsVersion, operation.SessionEpoch + 1, now, cancellationToken);
            if (ProofExpired()) return new(IdentityLinkStatus.AuthorityChanged, null);
            var completed = retry ? await db.ExternalIdentities.AsNoTracking().SingleOrDefaultAsync(
                x => x.Id == operation.OperationId && x.AccountId == operation.AccountId &&
                     x.Provider == operation.Provider && x.ProviderSubject == operation.Subject, cancellationToken) : null;
            return new(completed is null ? IdentityLinkStatus.AuthorityChanged : IdentityLinkStatus.AlreadyLinked, completed);
        }
        var confirmedMfa = await db.MfaSetups.AsNoTracking().Where(x => x.AccountId == operation.AccountId &&
            x.Status == MfaSetupStatus.Confirmed).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(cancellationToken);
        if (confirmedMfa != operation.ConfirmedMfaId) return new(IdentityLinkStatus.AuthorityChanged, null);
        var conflict = await FindConflictAsync(db, operation.AccountId, operation.Provider, operation.Subject, cancellationToken);
        if (conflict is not null) return conflict;
        var identity = new ExternalIdentity
        {
            Id = operation.OperationId,
            AccountId = operation.AccountId,
            Provider = operation.Provider,
            ProviderSubject = operation.Subject,
            LinkedAt = now,
        };
        db.ExternalIdentities.Add(identity);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await db.Accounts.Where(x => x.Id == operation.AccountId).ExecuteUpdateAsync(
                u => u.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1), cancellationToken);
            if (ProofExpired()) return new(IdentityLinkStatus.AuthorityChanged, null);
            await transaction.CommitAsync(cancellationToken);
            return new(IdentityLinkStatus.Linked, identity);
        }
        catch (DbUpdateException error) when (error.InnerException is not PostgresException pg || pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(cancellationToken);
            await using var read = await factory.CreateDbContextAsync(cancellationToken);
            var raced = await FindConflictAsync(read, operation.AccountId, operation.Provider, operation.Subject, cancellationToken);
            if (raced is null) throw;
            return raced;
        }
    }
    private static async Task<IdentityLinkResult?> FindConflictAsync(AuthDbContext db, AccountId accountId,
        string provider, string subject, CancellationToken cancellationToken)
    {
        var subjectLink = await db.ExternalIdentities.AsNoTracking().SingleOrDefaultAsync(
            x => x.Provider == provider && x.ProviderSubject == subject, cancellationToken);
        if (subjectLink is not null)
            return new(subjectLink.AccountId == accountId ? IdentityLinkStatus.AlreadyLinked : IdentityLinkStatus.SubjectTaken, subjectLink);
        var accountLink = await db.ExternalIdentities.AsNoTracking().SingleOrDefaultAsync(
            x => x.Provider == provider && x.AccountId == accountId, cancellationToken);
        return accountLink is null ? null : new(IdentityLinkStatus.AccountProviderTaken, accountLink);
    }
}
