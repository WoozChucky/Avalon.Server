using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Avalon.Database.Auth.Repositories;

public enum IdentityLinkStatus { Linked, AlreadyLinked, SubjectTaken, AccountProviderTaken }
public sealed record IdentityLinkResult(IdentityLinkStatus Status, ExternalIdentity? Identity);

public interface IExternalIdentityRepository
{
    Task<ExternalIdentity?> FindAsync(string provider, string subject, CancellationToken cancellationToken = default);
    Task<IdentityLinkResult> LinkAsync(AccountId accountId, string provider, string subject, DateTime now,
        CancellationToken cancellationToken = default);
}

public sealed class ExternalIdentityRepository(IDbContextFactory<AuthDbContext> factory) : IExternalIdentityRepository
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
        if (string.IsNullOrWhiteSpace(provider) || provider.Length > 16 ||
            string.IsNullOrWhiteSpace(subject) || subject.Length > 128)
            throw new ArgumentException("Invalid provider identity shape.");
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var conflict = await FindConflictAsync(db, accountId, provider, subject, cancellationToken);
        if (conflict is not null) return conflict;
        var identity = new ExternalIdentity
        {
            Id = Guid.NewGuid(), AccountId = accountId, Provider = provider, ProviderSubject = subject, LinkedAt = now,
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
