using System.Text;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Avalon.Server.Auth.UnitTests.Services;

/// <summary>
/// #555: an account can stay marked online after its connection is gone (a close whose write
/// failed, say), and nothing but the next start-up would clear it. The auth server now sweeps, on
/// an absolute-time due-check, every online row whose session is none of its live connections.
/// Real repository over SQLite for what the SQL decides; a substitute where only the calls matter.
/// </summary>
public sealed class OnlineSweepShould : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly AuthSqlite _database = new();
    private readonly AccountRepository _accounts;
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero));
    private readonly CapturingLogger _logger = new();
    private readonly List<Guid> _live = [];

    public OnlineSweepShould()
    {
        _accounts = new AccountRepository(_database);
    }

    public void Dispose() => _database.Dispose();

    private OnlineSweep Sweep(IAccountRepository accounts) => new(accounts, () => _live, Interval, _clock, _logger);

    private async Task<Account> AccountAsync() => await _accounts.CreateAsync(new Account
    {
        Username = "SWEEPUSER",
        Email = "sweep@example.com",
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
    public async Task Clear_an_online_account_whose_session_is_not_live_on_the_next_due_pass()
    {
        Account account = await AccountAsync();
        await _accounts.TryRecordLoginAsync(account.Id, "10.0.0.1", DateTime.UtcNow, Guid.NewGuid());
        OnlineSweep sweep = Sweep(_accounts);

        _clock.Advance(Interval);
        Assert.True(await sweep.RunIfDueAsync(CancellationToken.None));

        Account stored = await StoredAsync(account.Id);
        Assert.False(stored.Online);
        Assert.Null(stored.OnlineSessionId);
    }

    [Fact]
    public async Task Leave_an_online_account_whose_session_is_live()
    {
        Account account = await AccountAsync();
        Guid session = Guid.NewGuid();
        await _accounts.TryRecordLoginAsync(account.Id, "10.0.0.1", DateTime.UtcNow, session);
        _live.Add(session);
        OnlineSweep sweep = Sweep(_accounts);

        _clock.Advance(Interval);
        await sweep.RunIfDueAsync(CancellationToken.None);

        Account stored = await StoredAsync(account.Id);
        Assert.True(stored.Online);
        Assert.Equal(session, stored.OnlineSessionId);
    }

    [Fact]
    public async Task Clear_an_online_account_with_no_session_recorded()
    {
        Account account = await AccountAsync();
        await using (AuthDbContext context = _database.CreateDbContext())
        {
            await context.Accounts.Where(a => a.Id == account.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Online, true));
        }

        OnlineSweep sweep = Sweep(_accounts);
        _clock.Advance(Interval);
        await sweep.RunIfDueAsync(CancellationToken.None);

        Assert.False((await StoredAsync(account.Id)).Online);
    }

    [Fact]
    public async Task Keep_a_login_that_lands_between_the_read_and_the_write()
    {
        Account account = await AccountAsync();
        await _accounts.TryRecordLoginAsync(account.Id, "10.0.0.1", DateTime.UtcNow, Guid.NewGuid());
        Guid newer = Guid.NewGuid();
        var racing = new StaleAccountRepository(_accounts)
        {
            AfterOnlineRead = async () =>
            {
                await _accounts.TryRecordLoginAsync(account.Id, "10.0.0.2", DateTime.UtcNow, newer);
                _live.Add(newer);
            },
        };
        OnlineSweep sweep = Sweep(racing);

        _clock.Advance(Interval);
        await sweep.RunIfDueAsync(CancellationToken.None);

        Account stored = await StoredAsync(account.Id);
        Assert.True(stored.Online);
        Assert.Equal(newer, stored.OnlineSessionId);
    }

    [Fact]
    public async Task Pass_the_session_it_read_and_add_no_session_time()
    {
        var accounts = Substitute.For<IAccountRepository>();
        var id = new AccountId(7);
        Guid stale = Guid.NewGuid();
        accounts.ListOnlineSessionsAsync(Arg.Any<CancellationToken>())
            .Returns([new OnlineSession(id, stale)]);
        OnlineSweep sweep = Sweep(accounts);

        _clock.Advance(Interval);
        await sweep.RunIfDueAsync(CancellationToken.None);

        await accounts.Received(1).MarkOfflineAsync(id, stale, 0, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Do_nothing_before_the_interval_is_due()
    {
        var accounts = Substitute.For<IAccountRepository>();
        OnlineSweep sweep = Sweep(accounts);

        _clock.Advance(Interval - TimeSpan.FromMilliseconds(1));
        Assert.False(await sweep.RunIfDueAsync(CancellationToken.None));

        await accounts.DidNotReceive().ListOnlineSessionsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Catch_up_a_late_pass_once_not_once_per_missed_interval()
    {
        var accounts = Substitute.For<IAccountRepository>();
        accounts.ListOnlineSessionsAsync(Arg.Any<CancellationToken>()).Returns([]);
        OnlineSweep sweep = Sweep(accounts);

        _clock.Advance(Interval * 5);
        Assert.True(await sweep.RunIfDueAsync(CancellationToken.None));
        Assert.False(await sweep.RunIfDueAsync(CancellationToken.None));

        await accounts.Received(1).ListOnlineSessionsAsync(Arg.Any<CancellationToken>());
        Assert.Equal(_clock.GetUtcNow() + Interval, sweep.NextDue);
    }

    [Fact]
    public async Task Log_a_throwing_sweep_and_still_run_the_next_pass()
    {
        var accounts = Substitute.For<IAccountRepository>();
        accounts.ListOnlineSessionsAsync(Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("boom"));
        OnlineSweep sweep = Sweep(accounts);

        _clock.Advance(Interval);
        Assert.True(await sweep.RunIfDueAsync(CancellationToken.None));
        _clock.Advance(Interval);
        Assert.True(await sweep.RunIfDueAsync(CancellationToken.None));

        await accounts.Received(2).ListOnlineSessionsAsync(Arg.Any<CancellationToken>());
        Assert.Equal(2, _logger.Count(LogLevel.Error));
        Assert.Contains(_logger.Entries,
            e => e.Message.Contains(nameof(InvalidOperationException), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Stop_the_loop_cleanly_when_cancelled()
    {
        var accounts = Substitute.For<IAccountRepository>();
        accounts.ListOnlineSessionsAsync(Arg.Any<CancellationToken>()).Returns([]);
        var sweep = new OnlineSweep(accounts, () => _live, TimeSpan.FromMilliseconds(10), TimeProvider.System, _logger);
        using var stopping = new CancellationTokenSource();

        Task loop = sweep.RunAsync(TimeSpan.FromMilliseconds(5), stopping.Token);
        await Task.Delay(200);
        await stopping.CancelAsync();
        await loop.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(loop.IsCompletedSuccessfully);
        Assert.Equal(0, _logger.Count(LogLevel.Error));
        await accounts.Received().ListOnlineSessionsAsync(Arg.Any<CancellationToken>());
    }

    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
