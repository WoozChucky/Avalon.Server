using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Xunit;

namespace Avalon.Database.UnitTests;

public sealed class GameSessionReservationShould
{
    private static readonly DateTime s_now = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
    [Fact]
    public async Task Recover_only_the_exact_durable_reservation_without_extending_its_lease()
    {
        using var db = SqliteDatabase.Auth();
        Account account = await new AccountRepository(db).CreateAsync(StoreAuthenticationModelShould.Account("RETRY"));
        var repo = new GameSessionRepository(db);
        GameSessionReservation request = StoreAuthenticationModelShould.Reservation(account.Id, 0) with { GameContextId = Guid.NewGuid(), AdmissionExpiresAt = s_now.AddSeconds(30) };
        GameSession? first = await repo.TryReserveAsync(request, s_now);
        GameSession? retry = await repo.TryReserveAsync(request, s_now.AddSeconds(10));
        Assert.NotNull(retry);
        Assert.Equal(first!.GameSessionId, retry!.GameSessionId);
        Assert.Equal(first.FencingToken, retry.FencingToken);
        Assert.Equal(first.LeaseUntil, retry.LeaseUntil);
        Assert.Null(await repo.TryReserveAsync(request with { ServerId = "world-other" }, s_now));
        Assert.Null(await repo.TryReserveAsync(request with { GameContextId = Guid.NewGuid() }, s_now));
        Assert.Null(await repo.TryReserveAsync(request with { WorldId = 2 }, s_now));
    }
    /// <summary>
    /// A pending reservation that runs out, or that its world ends after a failed admission (#1013, through either end),
    /// never became the writer: the next reservation replaces it without a takeover and keeps the last active session as
    /// its ancestor, whose world's save barrier it must advance. An ended one can no longer activate, not even for an API
    /// replica whose clock lags behind the one that ended it.
    /// </summary>
    [Theory]
    [InlineData("ran-out")]
    [InlineData("ended")]
    [InlineData("expired")]
    public async Task Replace_expired_or_ended_pending_reservations_without_forgetting_the_prior_active_world(string how)
    {
        using var db = SqliteDatabase.Auth();
        Account account = await new AccountRepository(db).CreateAsync(StoreAuthenticationModelShould.Account("PENDING"));
        var repo = new GameSessionRepository(db);
        GameSession? active = await repo.TryReserveAsync(StoreAuthenticationModelShould.Reservation(account.Id, 0), s_now);
        Assert.True(await repo.TryActivateAsync(account.Id, active!.GameSessionId, 1, s_now, s_now.AddSeconds(45)));
        GameSession? pending = await repo.TryReserveAsync(StoreAuthenticationModelShould.Reservation(account.Id, 1) with { Takeover = true, WorldId = 2, ServerId = "world-2" }, s_now);
        Assert.NotNull(pending);
        DateTime next = s_now.AddSeconds(46);
        if (how != "ran-out")
        {
            Assert.True(how == "ended"
                ? await repo.TryEndAsync(account.Id, pending!.GameSessionId, 2, s_now.AddSeconds(1))
                : await repo.TryExpirePendingAsync(account.Id, pending!.GameSessionId, 2));
            Assert.False(await repo.TryActivateAsync(account.Id, pending.GameSessionId, 2, s_now, s_now.AddSeconds(30)));
            next = s_now.AddSeconds(2);
        }
        GameSession? replacement = await repo.TryReserveAsync(StoreAuthenticationModelShould.Reservation(account.Id, 2) with { WorldId = 3, ServerId = "world-3" }, next);
        Assert.NotNull(replacement);
        Assert.Equal(3, replacement!.FencingToken);
        Assert.Equal(active.GameSessionId, replacement.PreviousGameSessionId);
        Assert.Equal((ushort)1, replacement.PreviousWorldId);
    }
    [Fact]
    public async Task Reject_admission_that_expires_while_waiting_for_the_database()
    {
        using var db = SqliteDatabase.Auth();
        Account account = await new AccountRepository(db).CreateAsync(StoreAuthenticationModelShould.Account("EXPIRES"));
        var clock = new FixedClock(s_now.AddSeconds(30));
        var repo = new GameSessionRepository(db, clock);
        Assert.Null(await repo.TryReserveAsync(StoreAuthenticationModelShould.Reservation(account.Id, 0) with { AdmissionExpiresAt = s_now.AddSeconds(30) }, s_now));
        Assert.Null(await repo.FindAsync(account.Id));
    }
    private sealed class FixedClock(DateTime now) : TimeProvider { public override DateTimeOffset GetUtcNow() => new(now); }
}
