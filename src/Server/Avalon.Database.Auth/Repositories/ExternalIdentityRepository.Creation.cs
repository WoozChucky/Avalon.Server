using Avalon.Common.GameAuth;
using Avalon.Common.Accounts;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Avalon.Database.Auth.Repositories;

public sealed record StoreAccountCreationOperation(Guid OperationId, Account Account, string Subject, DateTime ProofExpiresAt)
{
    public string Provider { get; init; } = StoreProviders.Steam;
}

public sealed partial class ExternalIdentityRepository
{
    public Task<IdentityLinkResult> CreateAccountWithSteamLinkAsync(StoreAccountCreationOperation operation, DateTime now, CancellationToken ct = default) =>
        CreateAccountWithStoreLinkAsync(operation with { Provider = StoreProviders.Steam }, now, ct);

    public async Task<IdentityLinkResult> CreateAccountWithStoreLinkAsync(StoreAccountCreationOperation operation,
        DateTime now, CancellationToken cancellationToken = default)
    {
        if (operation.OperationId == Guid.Empty || string.IsNullOrWhiteSpace(operation.Provider) || operation.Provider != operation.Provider.Trim() || operation.Provider.Length > 32 ||
            string.IsNullOrWhiteSpace(operation.Subject) || operation.Subject != operation.Subject.Trim() || operation.Subject.Length > 128 ||
            string.IsNullOrWhiteSpace(operation.Account.Username) ||
            (operation.Account.IsStoreGenerated
                ? operation.Account.Email is not null || operation.Account.Salt.Length != 0 || operation.Account.Verifier.Length != 0
                : !AccountEmail.IsValid(operation.Account.Email) || operation.Account.Salt.Length == 0 || operation.Account.Verifier.Length == 0))
            throw new ArgumentException("Invalid store account creation operation.");
        bool ProofExpired() => operation.ProofExpiresAt.Kind != DateTimeKind.Utc ||
            operation.ProofExpiresAt <= (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (ProofExpired()) return new(IdentityLinkStatus.AuthorityChanged, null);
        var retry = await CreationRetryAsync(db, operation, cancellationToken);
        if (retry is not null) return retry;
        if (await db.ExternalIdentities.AnyAsync(x => x.Provider == operation.Provider && x.ProviderSubject == operation.Subject, cancellationToken))
            return await CreationRetryAsync(db, operation, cancellationToken) ?? new(IdentityLinkStatus.SubjectTaken, null);
        if (await db.Accounts.AnyAsync(x => x.Username == operation.Account.Username, cancellationToken))
            return await CreationRetryAsync(db, operation, cancellationToken) ?? new(IdentityLinkStatus.UsernameTaken, null);
        if (operation.Account.Email is not null && await db.Accounts.AnyAsync(x => x.Email == operation.Account.Email, cancellationToken))
            return await CreationRetryAsync(db, operation, cancellationToken) ?? new(IdentityLinkStatus.EmailTaken, null);
        // The caller supplies account details. Privileges and initial authority are fixed here.
        var account = new Account
        {
            Username = operation.Account.Username, Email = operation.Account.Email, IsStoreGenerated = operation.Account.IsStoreGenerated,
            Salt = operation.Account.Salt.ToArray(), Verifier = operation.Account.Verifier.ToArray(),
            JoinDate = now, LastLogin = now, LastIp = operation.Account.LastIp,
            Status = AccountStatus.Active, AccessLevel = AccountAccessLevel.Player, SessionEpoch = 1,
        };
        db.Accounts.Add(account);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            var identity = new ExternalIdentity
            {
                Id = operation.OperationId, AccountId = account.Id, Provider = operation.Provider,
                ProviderSubject = operation.Subject, LinkedAt = now,
            };
            db.ExternalIdentities.Add(identity);
            db.StoreAccountCreations.Add(new StoreAccountCreation
            {
                Id = operation.OperationId, AccountId = account.Id, Provider = operation.Provider, ProviderSubject = operation.Subject,
                CreatedAt = now, ProofExpiresAt = operation.ProofExpiresAt,
            });
            await db.SaveChangesAsync(cancellationToken);
            if (ProofExpired()) return new(IdentityLinkStatus.AuthorityChanged, null);
            await transaction.CommitAsync(cancellationToken);
            return new(IdentityLinkStatus.Linked, identity);
        }
        catch (DbUpdateException error) when (error.InnerException is not PostgresException pg || pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(cancellationToken);
            await using var read = await factory.CreateDbContextAsync(cancellationToken);
            var raced = await CreationRetryAsync(read, operation, cancellationToken);
            if (raced is not null) return raced;
            if (await read.ExternalIdentities.AnyAsync(x => x.Provider == operation.Provider && x.ProviderSubject == operation.Subject, cancellationToken))
                return new(IdentityLinkStatus.SubjectTaken, null);
            if (await read.Accounts.AnyAsync(x => x.Username == operation.Account.Username, cancellationToken))
                return new(IdentityLinkStatus.UsernameTaken, null);
            if (operation.Account.Email is not null && await read.Accounts.AnyAsync(x => x.Email == operation.Account.Email, cancellationToken))
                return new(IdentityLinkStatus.EmailTaken, null);
            throw;
        }
    }

    private static async Task<IdentityLinkResult?> CreationRetryAsync(AuthDbContext db, StoreAccountCreationOperation operation,
        CancellationToken cancellationToken)
    {
        var receipt = await db.StoreAccountCreations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == operation.OperationId, cancellationToken);
        if (receipt is null) return null;
        if (receipt.Provider != operation.Provider || receipt.ProviderSubject != operation.Subject) return new(IdentityLinkStatus.AuthorityChanged, null);
        var identity = await db.ExternalIdentities.AsNoTracking().SingleOrDefaultAsync(x => x.Id == operation.OperationId &&
            x.AccountId == receipt.AccountId && x.Provider == operation.Provider && x.ProviderSubject == operation.Subject, cancellationToken);
        var root = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == receipt.AccountId, cancellationToken);
        return identity is not null && root is { CredentialsVersion: 0, SessionEpoch: 1, Status: AccountStatus.Active } &&
               root.Username == operation.Account.Username && root.Email == operation.Account.Email &&
               root.IsStoreGenerated == operation.Account.IsStoreGenerated
            ? new(IdentityLinkStatus.AlreadyLinked, identity) : new(IdentityLinkStatus.AuthorityChanged, null);
    }
}
