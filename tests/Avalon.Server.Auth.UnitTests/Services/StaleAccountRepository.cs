using System.Linq.Expressions;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;

namespace Avalon.Server.Auth.UnitTests.Services;

/// <summary>
/// Forwards to the real repository, but can hand out reads taken earlier (a request that read
/// the row before another wrote it) and run a write of its own right after a read.
/// </summary>
internal sealed class StaleAccountRepository(IAccountRepository inner) : IAccountRepository
{
    public Queue<Account> UsernameReads { get; } = new();

    public Func<Task>? AfterRead { get; init; }

    public async Task<Account?> FindByUserNameAsync(string userName, CancellationToken cancellationToken = default)
    {
        Account? account = UsernameReads.Count > 0
            ? UsernameReads.Dequeue()
            : await inner.FindByUserNameAsync(userName, cancellationToken);
        if (AfterRead != null) await AfterRead();
        return account;
    }

    public async Task<Account?> FindByIdAsync(AccountId id, bool track = false, CancellationToken cancellationToken = default)
    {
        Account? account = await inner.FindByIdAsync(id, track, cancellationToken);
        if (AfterRead != null) await AfterRead();
        return account;
    }

    public Task<Account?> FindByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        inner.FindByEmailAsync(email, cancellationToken);

    public Task<FailedLoginResult> RecordFailedLoginAsync(AccountId id, string attemptIp, DateTime now,
        DateTime? lockUntil, CancellationToken cancellationToken = default) =>
        inner.RecordFailedLoginAsync(id, attemptIp, now, lockUntil, cancellationToken);

    public Task<bool> TryRecordLoginAsync(AccountId id, string lastIp, DateTime now,
        CancellationToken cancellationToken = default) =>
        inner.TryRecordLoginAsync(id, lastIp, now, cancellationToken);

    public Task MarkOfflineAsync(AccountId id, long sessionSeconds = 0, CancellationToken cancellationToken = default) =>
        inner.MarkOfflineAsync(id, sessionSeconds, cancellationToken);

    public Task MarkAllOfflineAsync(CancellationToken cancellationToken = default) =>
        inner.MarkAllOfflineAsync(cancellationToken);

    public Task SetSessionKeyAsync(AccountId id, byte[] sessionKey, CancellationToken cancellationToken = default) =>
        inner.SetSessionKeyAsync(id, sessionKey, cancellationToken);

    public Task<PagedResult<Account>> PaginateAsync(EntityPaginateFilter<Account> filter, bool track = false,
        CancellationToken cancellationToken = default) => inner.PaginateAsync(filter, track, cancellationToken);

    public Task<List<Account>> FindAllAsync(bool track = false, CancellationToken cancellationToken = default) =>
        inner.FindAllAsync(track, cancellationToken);

    public Task<List<Account>> FindByAsync(Expression<Func<Account, bool>> predicate,
        CancellationToken cancellationToken = default) => inner.FindByAsync(predicate, cancellationToken);

    public Task<Account> CreateAsync(Account entity, CancellationToken cancellationToken = default) =>
        inner.CreateAsync(entity, cancellationToken);

    public Task<List<Account>> CreateAsync(List<Account> entities, CancellationToken cancellationToken = default) =>
        inner.CreateAsync(entities, cancellationToken);

    public Task<Account> UpdateAsync(Account entity, CancellationToken cancellationToken = default) =>
        inner.UpdateAsync(entity, cancellationToken);

    public Task DeleteAsync(AccountId id, CancellationToken cancellationToken = default) =>
        inner.DeleteAsync(id, cancellationToken);
}
