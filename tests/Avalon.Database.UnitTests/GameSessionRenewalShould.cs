using Avalon.Database.Auth.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Avalon.Database.UnitTests;

public sealed class GameSessionRenewalShould
{
    private static readonly DateTime Now = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
    [Fact]
    public async Task Renew_only_the_live_server_session_and_current_account_authority()
    {
        using var database = SqliteDatabase.Auth();
        var account = await new AccountRepository(database).CreateAsync(StoreAuthenticationModelShould.Account("RENEW"));
        var repo = new GameSessionRepository(database);
        var head = (await repo.TryReserveAsync(StoreAuthenticationModelShould.Reservation(account.Id, 0), Now))!;
        Assert.True(await repo.TryActivateAsync(account.Id, head.GameSessionId, 1, Now, Now.AddSeconds(45)));
        Assert.False(await repo.TryRenewAsync(account.Id, head.GameSessionId, 1, "wrong-server", 0, 0, Now.AddSeconds(15), Now.AddSeconds(60), Now.AddMinutes(5)));
        Assert.False(await repo.TryRenewAsync(account.Id, Guid.NewGuid(), 1, "world-1", 0, 0, Now.AddSeconds(15), Now.AddSeconds(60), Now.AddMinutes(5)));
        Assert.False(await repo.TryRenewAsync(account.Id, head.GameSessionId, 2, "world-1", 0, 0, Now.AddSeconds(15), Now.AddSeconds(60), Now.AddMinutes(5)));
        Assert.True(await repo.TryRenewAsync(account.Id, head.GameSessionId, 1, "world-1", 0, 0, Now.AddSeconds(15), Now.AddSeconds(60), Now.AddMinutes(5)));
        Assert.Equal(Now.AddSeconds(60), (await repo.FindAsync(account.Id))!.LeaseUntil);
        await using (var db = database.CreateDbContext())
            await db.Accounts.Where(a => a.Id == account.Id).ExecuteUpdateAsync(u => u.SetProperty(a => a.SessionEpoch, 1));
        Assert.False(await repo.TryRenewAsync(account.Id, head.GameSessionId, 1, "world-1", 0, 0, Now.AddSeconds(30), Now.AddSeconds(75), Now.AddMinutes(5)));
    }
    [Fact]
    public async Task Never_resurrect_an_expired_lease_or_exceed_lease_and_license_bounds()
    {
        using var database = SqliteDatabase.Auth();
        var account = await new AccountRepository(database).CreateAsync(StoreAuthenticationModelShould.Account("EXPIREDRENEW"));
        var repo = new GameSessionRepository(database);
        var head = (await repo.TryReserveAsync(StoreAuthenticationModelShould.Reservation(account.Id, 0), Now))!;
        Assert.True(await repo.TryActivateAsync(account.Id, head.GameSessionId, 1, Now, Now.AddSeconds(45)));
        Assert.False(await repo.TryRenewAsync(account.Id, head.GameSessionId, 1, "world-1", 0, 0, Now, Now.AddSeconds(46), Now.AddMinutes(5)));
        Assert.False(await repo.TryRenewAsync(account.Id, head.GameSessionId, 1, "world-1", 0, 0, Now, Now.AddSeconds(45), Now.AddSeconds(44)));
        Assert.False(await repo.TryRenewAsync(account.Id, head.GameSessionId, 1, "world-1", 0, 0, Now, Now.AddSeconds(45), Now.AddMinutes(6)));
        Assert.False(await repo.TryRenewAsync(account.Id, head.GameSessionId, 1, "world-1", 0, 0, Now.AddSeconds(45), Now.AddSeconds(90), Now.AddMinutes(5)));
    }
}
