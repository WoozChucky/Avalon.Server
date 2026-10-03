using Avalon.Common.ValueObjects;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Avalon.Database.UnitTests;

public class StoreAuthenticationModelShould
{
    private static readonly DateTime Now = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Enforce_both_identity_uniqueness_boundaries_and_keep_the_exact_subject()
    {
        using var database = SqliteDatabase.Auth();
        var accounts = new AccountRepository(database);
        var first = await accounts.CreateAsync(Account("ONE"));
        var second = await accounts.CreateAsync(Account("TWO"));
        var links = new ExternalIdentityRepository(database);
        Assert.Equal(IdentityLinkStatus.Linked, (await links.LinkAsync(first.Id, "steam", "00076561198000000001", Now)).Status);
        Assert.Equal(first.Id, (await links.FindAsync("steam", "00076561198000000001"))!.AccountId);
        Assert.Null(await links.FindAsync("steam", "76561198000000001"));
        Assert.Equal(IdentityLinkStatus.AlreadyLinked, (await links.LinkAsync(first.Id, "steam", "00076561198000000001", Now)).Status);
        Assert.Equal(IdentityLinkStatus.SubjectTaken, (await links.LinkAsync(second.Id, "steam", "00076561198000000001", Now)).Status);
        Assert.Equal(IdentityLinkStatus.AccountProviderTaken, (await links.LinkAsync(first.Id, "steam", "76561198000000002", Now)).Status);
        await using var context = database.CreateDbContext();
        Assert.Equal(1, await context.ExternalIdentities.CountAsync());
    }

    [Fact]
    public async Task Keep_historical_license_evidence_without_granting_expired_ownership()
    {
        using var database = SqliteDatabase.Auth();
        var account = await new AccountRepository(database).CreateAsync(Account("LICENSE"));
        var licenses = new LicenseObservationRepository(database);
        var observed = new LicenseObservation
        {
            Id = Guid.NewGuid(), AccountId = account.Id, Provider = "steam", ProviderSubject = "76561198000000001",
            Environment = "production", Product = "avalon.base", OwnsProduct = true,
            ObservedAt = Now.AddMinutes(-6), AuthorizedUntil = Now.AddMinutes(-1), PolicyVersion = 1,
            ProviderAppId = "2499460", ProviderOwnerSubject = "76561198000000002", Permanent = false,
        };
        await licenses.RecordAsync(observed);
        Assert.False(observed.Authorizes("production", "avalon.base", Now));
        Assert.False(observed.Authorizes("development", "avalon.base", Now.AddMinutes(-2)));
        Assert.False(observed.Authorizes("production", "another.product", Now.AddMinutes(-2)));
        Assert.True(observed.Authorizes("production", "avalon.base", Now.AddMinutes(-2)));
        await using var context = database.CreateDbContext();
        Assert.Equal(observed.ProviderOwnerSubject, (await context.LicenseObservations.SingleAsync()).ProviderOwnerSubject);
    }

    [Fact]
    public async Task Reserve_once_and_advance_the_fence_only_for_an_explicit_takeover()
    {
        using var database = SqliteDatabase.Auth();
        var account = await new AccountRepository(database).CreateAsync(Account("SESSION"));
        var sessions = new GameSessionRepository(database);
        var first = await sessions.TryReserveAsync(Reservation(account.Id, 0), Now);
        Assert.NotNull(first);
        Assert.Equal(1, first!.FencingToken);
        Assert.Null(await sessions.TryReserveAsync(Reservation(account.Id, 0), Now));
        Assert.Null(await sessions.TryReserveAsync(Reservation(account.Id, 1), Now));
        Assert.True(await sessions.TryActivateAsync(account.Id, first.GameSessionId, 1, Now, Now.AddSeconds(45)));
        Assert.Null(await sessions.TryReserveAsync(Reservation(account.Id, 1), Now));
        var takeover = await sessions.TryReserveAsync(Reservation(account.Id, 1) with { Takeover = true }, Now);
        Assert.NotNull(takeover);
        Assert.Equal(2, takeover!.FencingToken);
        Assert.Equal(first.GameSessionId, takeover.PreviousGameSessionId);
        Assert.False(await sessions.TryActivateAsync(account.Id, first.GameSessionId, 1, Now, Now.AddSeconds(45)));
        Assert.Null(await sessions.TryReserveAsync(Reservation(account.Id, 2) with { Takeover = true }, Now));
    }

    [Fact]
    public async Task Refuse_a_reservation_with_stale_account_authority()
    {
        using var database = SqliteDatabase.Auth();
        var account = await new AccountRepository(database).CreateAsync(Account("REVOKED"));
        await using (var context = database.CreateDbContext())
            await context.Accounts.Where(a => a.Id == account.Id).ExecuteUpdateAsync(u => u.SetProperty(a => a.SessionEpoch, 1));
        Assert.Null(await new GameSessionRepository(database).TryReserveAsync(Reservation(account.Id, 0), Now));
    }

    internal static GameSessionReservation Reservation(AccountId accountId, long fence) =>
        new(accountId, fence, Guid.NewGuid(), "world-1", 1, "production", 0, 0, Now.AddMinutes(5), false);

    internal static Account Account(string name) => new()
    {
        Username = name, Email = name.ToLowerInvariant() + "@example.test", Salt = [1], Verifier = [2], JoinDate = Now,
    };
}
