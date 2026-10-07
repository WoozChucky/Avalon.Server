using Avalon.World.GameAuth;
using Microsoft.Extensions.Time.Testing;

namespace Avalon.Server.World.UnitTests.GameAuth;

public sealed class GameSessionLeaseShould
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero));
    private SessionLeaseResponse Reply() => new()
    {
        State = "active",
        AccountId = "42",
        GameSessionId = Guid.NewGuid().ToString("D"),
        GameContextId = Guid.NewGuid().ToString("D"),
        FencingToken = "7",
        ServerId = "world-one",
        WorldId = 1,
        AccessLevel = 1,
        CredentialsVersion = 3,
        SessionEpoch = "9",
        LeaseUntil = _clock.GetUtcNow().UtcDateTime.AddSeconds(45),
        AuthorizationUntil = _clock.GetUtcNow().UtcDateTime.AddMinutes(5)
    };

    [Fact]
    public void Never_admit_pending_or_expired_or_wrong_workload_authority()
    {
        var reply = Reply();
        Assert.Null(GameSessionLease.TryCreate(reply with { State = "pending" }, "world-one", 1, _clock));
        Assert.Null(GameSessionLease.TryCreate(reply with { LeaseUntil = _clock.GetUtcNow().UtcDateTime }, "world-one", 1, _clock));
        Assert.Null(GameSessionLease.TryCreate(reply, "other-server", 1, _clock));
        Assert.Null(GameSessionLease.TryCreate(reply, "world-one", 2, _clock));
        Assert.Null(GameSessionLease.TryCreate(reply with { LeaseUntil = _clock.GetUtcNow().UtcDateTime.AddSeconds(46) }, "world-one", 1, _clock));
    }

    [Fact]
    public void A_late_successful_heartbeat_cannot_resurrect_an_expired_session()
    {
        var reply = Reply();
        var lease = Assert.IsType<GameSessionLease>(GameSessionLease.TryCreate(reply, "world-one", 1, _clock));
        _clock.Advance(TimeSpan.FromSeconds(45));
        Assert.False(lease.IsActive);
        Assert.False(lease.TryRenew(reply with { LeaseUntil = _clock.GetUtcNow().UtcDateTime.AddSeconds(45) }));
    }

    [Fact]
    public void Refuse_identity_context_fence_and_account_version_changes_without_rebinding()
    {
        var reply = Reply();
        var lease = Assert.IsType<GameSessionLease>(GameSessionLease.TryCreate(reply, "world-one", 1, _clock));
        Assert.False(lease.TryRenew(reply with { FencingToken = "8" }));
        Assert.False(lease.TryRenew(reply with { AccountId = "43" }));
        Assert.False(lease.TryRenew(reply with { GameContextId = Guid.NewGuid().ToString("D") }));
        Assert.False(lease.TryRenew(reply with { SessionEpoch = "10" }));
        Assert.False(lease.TryRenew(reply with { CredentialsVersion = 4 }));
        Assert.Equal(42, lease.Authority.AccountId.Value);
        Assert.Equal(7, lease.Authority.FencingToken);
    }

    [Fact]
    public void Revocation_wins_over_concurrent_renewal()
    {
        var reply = Reply();
        var lease = Assert.IsType<GameSessionLease>(GameSessionLease.TryCreate(reply, "world-one", 1, _clock));
        Parallel.Invoke(lease.Revoke, () => lease.TryRenew(reply));
        Assert.False(lease.IsActive);
        Assert.False(lease.TryRenew(reply));
    }

    [Fact]
    public void Accept_the_initial_zero_epoch_of_an_existing_account_when_the_backend_grants_it()
    {
        Assert.NotNull(GameSessionLease.TryCreate(Reply() with { SessionEpoch = "0" }, "world-one", 1, _clock));
        Assert.Null(GameSessionLease.TryCreate(Reply() with { SessionEpoch = "-1" }, "world-one", 1, _clock));
    }

    [Fact]
    public void Renewal_has_one_immutable_writer_and_is_capped_by_license()
    {
        var reply = Reply();
        var lease = Assert.IsType<GameSessionLease>(GameSessionLease.TryCreate(reply, "world-one", 1, _clock));
        _clock.Advance(TimeSpan.FromSeconds(15));
        var renewal = reply with { LeaseUntil = _clock.GetUtcNow().UtcDateTime.AddSeconds(45) };
        Assert.True(lease.TryRenew(renewal));
        Assert.False(lease.TryRenew(renewal with { AuthorizationUntil = renewal.LeaseUntil!.Value.AddSeconds(-1) }));
        _clock.Advance(TimeSpan.FromSeconds(44));
        Assert.True(lease.IsActive);
        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(lease.IsActive);
    }
}
