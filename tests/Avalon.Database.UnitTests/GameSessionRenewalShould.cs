using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Avalon.Database.UnitTests;

public sealed class GameSessionRenewalShould
{
    private static readonly DateTime s_now = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
    [Fact]
    public async Task Renew_only_the_live_server_session_and_current_account_authority()
    {
        using var database = SqliteDatabase.Auth();
        Account account = await new AccountRepository(database).CreateAsync(StoreAuthenticationModelShould.Account("RENEW"));
        var repo = new GameSessionRepository(database);
        GameSession head = (await repo.TryReserveAsync(StoreAuthenticationModelShould.Reservation(account.Id, 0), s_now))!;
        Assert.True(await repo.TryActivateAsync(account.Id, head.GameSessionId, 1, s_now, s_now.AddSeconds(45)));
        Assert.False(await repo.TryRenewAsync(account.Id, head.GameSessionId, 1, "wrong-server", 0, 0, s_now.AddSeconds(15), s_now.AddSeconds(60), s_now.AddMinutes(5)));
        Assert.False(await repo.TryRenewAsync(account.Id, Guid.NewGuid(), 1, "world-1", 0, 0, s_now.AddSeconds(15), s_now.AddSeconds(60), s_now.AddMinutes(5)));
        Assert.False(await repo.TryRenewAsync(account.Id, head.GameSessionId, 2, "world-1", 0, 0, s_now.AddSeconds(15), s_now.AddSeconds(60), s_now.AddMinutes(5)));
        Assert.True(await repo.TryRenewAsync(account.Id, head.GameSessionId, 1, "world-1", 0, 0, s_now.AddSeconds(15), s_now.AddSeconds(60), s_now.AddMinutes(5)));
        Assert.Equal(s_now.AddSeconds(60), (await repo.FindAsync(account.Id))!.LeaseUntil);
        await using (AuthDbContext db = database.CreateDbContext())
            await db.Accounts.Where(a => a.Id == account.Id).ExecuteUpdateAsync(u => u.SetProperty(a => a.SessionEpoch, 1));
        Assert.False(await repo.TryRenewAsync(account.Id, head.GameSessionId, 1, "world-1", 0, 0, s_now.AddSeconds(30), s_now.AddSeconds(75), s_now.AddMinutes(5)));
    }
    /// <summary>
    /// #882: the account row's game-authority hold still refused a locked account, so five wrong website passwords
    /// refused the join ticket, world entry and the next heartbeat. The lock guards the password steps only.
    /// </summary>
    [Fact]
    public async Task Reserve_activate_and_renew_a_session_while_a_password_guesser_holds_the_lock()
    {
        using var database = SqliteDatabase.Auth();
        Account account = await new AccountRepository(database).CreateAsync(StoreAuthenticationModelShould.Account("LOCKEDPLAYER"));
        await using (AuthDbContext db = database.CreateDbContext())
        {
            await db.Accounts.Where(a => a.Id == account.Id).ExecuteUpdateAsync(u => u
                .SetProperty(a => a.Locked, true).SetProperty(a => a.LockedUntil, (DateTime?)s_now.AddMinutes(15)));
        }

        var repo = new GameSessionRepository(database);
        GameSession? head = await repo.TryReserveAsync(StoreAuthenticationModelShould.Reservation(account.Id, 0), s_now);

        Assert.NotNull(head);
        Assert.True(await repo.TryActivateAsync(account.Id, head.GameSessionId, 1, s_now, s_now.AddSeconds(45)));
        Assert.True(await repo.TryRenewAsync(account.Id, head.GameSessionId, 1, "world-1", 0, 0, s_now.AddSeconds(15), s_now.AddSeconds(60), s_now.AddMinutes(5)));
    }

    [Fact]
    public async Task Never_resurrect_an_expired_lease_or_exceed_lease_and_license_bounds()
    {
        using var database = SqliteDatabase.Auth();
        Account account = await new AccountRepository(database).CreateAsync(StoreAuthenticationModelShould.Account("EXPIREDRENEW"));
        var repo = new GameSessionRepository(database);
        GameSession head = (await repo.TryReserveAsync(StoreAuthenticationModelShould.Reservation(account.Id, 0), s_now))!;
        Assert.True(await repo.TryActivateAsync(account.Id, head.GameSessionId, 1, s_now, s_now.AddSeconds(45)));
        Assert.False(await repo.TryRenewAsync(account.Id, head.GameSessionId, 1, "world-1", 0, 0, s_now, s_now.AddSeconds(46), s_now.AddMinutes(5)));
        Assert.False(await repo.TryRenewAsync(account.Id, head.GameSessionId, 1, "world-1", 0, 0, s_now, s_now.AddSeconds(45), s_now.AddSeconds(44)));
        Assert.False(await repo.TryRenewAsync(account.Id, head.GameSessionId, 1, "world-1", 0, 0, s_now, s_now.AddSeconds(45), s_now.AddMinutes(6)));
        Assert.False(await repo.TryRenewAsync(account.Id, head.GameSessionId, 1, "world-1", 0, 0, s_now.AddSeconds(45), s_now.AddSeconds(90), s_now.AddMinutes(5)));
    }
}
