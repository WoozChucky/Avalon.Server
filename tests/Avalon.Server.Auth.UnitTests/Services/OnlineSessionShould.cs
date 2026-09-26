using System.Text;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Server.Auth.UnitTests.Services;

/// <summary>
/// #487: a connection's close marked its account offline whatever had happened since, so a close
/// landing after a newer session of the same account had logged in marked that session offline.
/// The login now records which connection set <c>Online</c>, and a close clears it only while it
/// is still that connection's. Real repository, real relational schema: the fix is in the SQL.
/// </summary>
public sealed class OnlineSessionShould : IDisposable
{
    private readonly AuthSqlite _database = new();
    private readonly AccountRepository _accounts;

    public OnlineSessionShould()
    {
        _accounts = new AccountRepository(_database);
    }

    public void Dispose() => _database.Dispose();

    private async Task<Account> AccountAsync() => await _accounts.CreateAsync(new Account
    {
        Username = "SESSIONUSER",
        Email = "session@example.com",
        Salt = new byte[16],
        Verifier = Encoding.UTF8.GetBytes("unused"),
        JoinDate = DateTime.UtcNow,
        LastLogin = DateTime.UtcNow,
    });

    private async Task<Account> StoredAsync(AccountId id)
    {
        await using AuthDbContext context = _database.CreateDbContext();
        return await context.Accounts.AsNoTracking().SingleAsync(a => a.Id == id);
    }

    [Fact]
    public async Task Keep_the_account_online_when_an_older_connection_closes_after_a_newer_one_logged_in()
    {
        Account account = await AccountAsync();
        Guid connectionA = Guid.NewGuid();
        Guid connectionB = Guid.NewGuid();
        DateTime now = DateTime.UtcNow;

        Assert.True(await _accounts.TryRecordLoginAsync(account.Id, "10.0.0.1", now, connectionA));
        Assert.True(await _accounts.TryRecordLoginAsync(account.Id, "10.0.0.2", now.AddSeconds(5), connectionB));

        await AuthConnection.RecordDisconnectAsync(_accounts, account.Id, connectionA, now.AddSeconds(10));

        Account stored = await StoredAsync(account.Id);
        Assert.True(stored.Online);
        Assert.Equal(connectionB, stored.OnlineSessionId);
    }

    [Fact]
    public async Task Mark_the_account_offline_when_the_connection_that_is_online_closes()
    {
        Account account = await AccountAsync();
        Guid connectionA = Guid.NewGuid();
        Guid connectionB = Guid.NewGuid();
        DateTime now = DateTime.UtcNow;
        await _accounts.TryRecordLoginAsync(account.Id, "10.0.0.1", now, connectionA);
        await _accounts.TryRecordLoginAsync(account.Id, "10.0.0.2", now, connectionB);
        await AuthConnection.RecordDisconnectAsync(_accounts, account.Id, connectionA, now.AddSeconds(10));

        await AuthConnection.RecordDisconnectAsync(_accounts, account.Id, connectionB, now.AddSeconds(20));

        Account stored = await StoredAsync(account.Id);
        Assert.False(stored.Online);
        Assert.Null(stored.OnlineSessionId);
    }

    [Fact]
    public async Task Still_count_a_stale_connection_session_time()
    {
        Account account = await AccountAsync();
        Guid connectionA = Guid.NewGuid();
        DateTime now = DateTime.UtcNow;
        await _accounts.TryRecordLoginAsync(account.Id, "10.0.0.1", now, connectionA);
        await _accounts.TryRecordLoginAsync(account.Id, "10.0.0.2", now, Guid.NewGuid());

        await _accounts.MarkOfflineAsync(account.Id, connectionA, sessionSeconds: 30);

        Account stored = await StoredAsync(account.Id);
        Assert.True(stored.Online);
        Assert.Equal(30, stored.TotalTime);
    }

    [Fact]
    public async Task Clear_every_online_session_at_start_up()
    {
        Account account = await AccountAsync();
        await _accounts.TryRecordLoginAsync(account.Id, "10.0.0.1", DateTime.UtcNow, Guid.NewGuid());

        await _accounts.MarkAllOfflineAsync();

        Account stored = await StoredAsync(account.Id);
        Assert.False(stored.Online);
        Assert.Null(stored.OnlineSessionId);
    }
}
