using Avalon.Api.Hosting.Worlds;
using Avalon.Api.Identity.Services;
using Avalon.Api.Services;
using Avalon.Api.Testing;
using Avalon.Common.GameAuth;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Auth;
using Avalon.Domain.Characters;
using Avalon.Infrastructure.GameAuth;
using Avalon.Infrastructure.WorldPlaces;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using StackExchange.Redis;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.GameAuth;

public sealed class GameSessionFenceServiceShould
{
    private readonly JoinHarness _h = new();
    private readonly IWorldRepositories _worlds = Substitute.For<IWorldRepositories>();
    private readonly IGameplayFenceRepository _previous = Substitute.For<IGameplayFenceRepository>();
    private readonly IGameplayFenceRepository _target = Substitute.For<IGameplayFenceRepository>();
    private readonly List<string> _order = [];
    private GameSession _head = null!;
    private GameSessionFenceService _service = null!;
    private async Task Arrange(uint? appId = null, ushort worldId = 1, bool native = false, IWorldPlaceStore? places = null)
    {
        GameAuthReply auth = native ? await _h.AuthenticateAvalon() : await _h.Authenticate(appId);
        GameContextRecord context = (await _h.Authorization.GetContextAsync(auth.GameContextCredential!, true, CancellationToken.None))!;
        DateTime now = _h.Clock.GetUtcNow().UtcDateTime;
        _head = new GameSession
        {
            AccountId = _h.Account.Id,
            GameSessionId = Guid.NewGuid(),
            GameContextId = context.Id,
            FencingToken = 2,
            ServerId = "world-" + worldId,
            WorldId = worldId,
            Environment = "production",
            State = GameSessionState.Pending,
            PreviousWorldId = 2,
            PreviousServerId = "world-2",
            PreviousGameSessionId = Guid.NewGuid(),
            CreatedAt = now,
            LeaseUntil = now.AddSeconds(45),
            LicenseUntil = now.AddMinutes(5)
        };
        _h.Sessions.FindAsync(_h.Account.Id, Arg.Any<CancellationToken>()).Returns(_head);
        _worlds.GameplayFences(new WorldId(worldId)).Returns(_target);
        _worlds.GameplayFences(new WorldId(2)).Returns(_previous);
        _previous.AdvanceAsync(Arg.Any<GameplayWriteAuthority>(), true, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(call => { _order.Add("previous"); return call.ArgAt<DateTime>(2); });
        _target.AdvanceAsync(Arg.Any<GameplayWriteAuthority>(), false, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(call => { _order.Add("target"); return call.ArgAt<DateTime>(2); });
        _h.Sessions.TryActivateAsync(_head.AccountId, _head.GameSessionId, 2, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(call =>
        { _order.Add("sql-active"); _head.State = GameSessionState.Active; _head.LeaseUntil = call.ArgAt<DateTime>(4); return true; });
        _target.ActivateAsync(Arg.Any<GameplayWriteAuthority>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(call => { _order.Add("target-active"); return call.ArgAt<DateTime>(1); });
        IAccountRepository accounts = Substitute.For<IAccountRepository>();
        accounts.FindByIdAsync(_h.Account.Id, false, Arg.Any<CancellationToken>()).Returns(_h.Account);
        _service = new(_h.Sessions, _h.Authorization, _worlds, accounts, Options.Create(new GameWorkloadConfiguration { Servers = [new GameServerDefinition { ServerId = "world-1", WorldId = 1 }, new GameServerDefinition { ServerId = "world-2", WorldId = 2 }, new GameServerDefinition { ServerId = "world-3", WorldId = 3 }] }), _h.Clock, new GameApplicationAccessPolicy(Options.Create(_h.Configuration)),
            places ?? _h.Places, new PlaceLedgerOutage(NullLogger<PlaceLedgerOutage>.Instance, _h.Clock));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Activation_and_heartbeat_recheck_the_actual_world_application_policy(ushort worldId)
    {
        await Arrange(2514590, worldId);
        _h.Account.AccessLevel |= Avalon.Common.Accounts.AccountAccessLevel.Admin;
        GameSessionLeaseReply first = await Activate("world-" + worldId);
        if (worldId == 1)
        {
            Assert.NotNull(first.Error);
            Assert.Empty(_order);
            return;
        }
        Assert.Null(first.Error);
        _h.Configuration.SteamPlaytest.AllowedWorldIds = [1];
        Assert.NotNull((await _service.HeartbeatAsync("world-3", _head.AccountId, _head.GameSessionId, 2, default)).Error);
        Assert.NotNull((await Activate("world-3")).Error);
        await _h.Sessions.DidNotReceive().TryRenewAsync(Arg.Any<Avalon.Common.ValueObjects.AccountId>(), Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<long>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        _h.Configuration.SteamPlaytest.AllowedWorldIds = [3];
        _h.Configuration.SteamPlaytest.Enabled = false;
        Assert.NotNull((await Activate("world-3")).Error);
    }
    private Task<GameSessionLeaseReply> Activate(string server = "world-1") => _service.ActivateAsync(server, _head.AccountId, _head.GameSessionId, 2, CancellationToken.None);
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Shared_license_revocation_refuses_heartbeat_without_extending_the_lease(bool native, bool outage)
    {
        await Arrange(native: native);
        Assert.Null((await Activate()).Error);
        DateTime until = _head.LeaseUntil;
        // An outage of the license store is not a revocation: the world keeps its lease (no kick) and asks again.
        if (outage) TestGameAuthorization.Licenses(_h.Store).FindFailure = new IOException("database down");
        else _h.RevokeLicense();
        Assert.Equal(outage ? GameAuthErrors.ServiceUnavailable : GameAuthErrors.SessionRevoked,
            (await _service.HeartbeatAsync("world-1", _head.AccountId, _head.GameSessionId, 2, default)).Error);
        Assert.Equal(until, _head.LeaseUntil);
        await _h.Sessions.DidNotReceive().TryRenewAsync(Arg.Any<Avalon.Common.ValueObjects.AccountId>(), Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<long>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _target.DidNotReceive().RenewAsync(Arg.Any<GameplayWriteAuthority>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }
    [Fact]
    public async Task Advance_both_character_database_barriers_before_publishing_active_authority()
    {
        await Arrange();
        GameSessionLeaseReply reply = await Activate();
        Assert.Null(reply.Error);
        Assert.Equal("active", reply.State);
        Assert.Equal(new[] { "previous", "target", "sql-active", "target-active" }, _order);
        Assert.Equal(_h.Clock.GetUtcNow().UtcDateTime.AddSeconds(45), reply.LeaseUntil);
    }
    /// <summary>
    /// #1009: the session's end is worked out on the API's clock, and the target guard checked it against the characters
    /// database's own, so an API clock running ahead of the database's refused every activation (BARRIER_PENDING) and
    /// every renewal. The guard's clock caps the end it holds, and the world is given no lease past that guard.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(-2)]
    public async Task Activate_and_renew_whichever_way_the_api_and_character_database_clocks_disagree(int apiAheadSeconds)
    {
        await Arrange();
        using var characters = new SqliteWorlds(1);
        var database = new OffsetClock(_h.Clock, TimeSpan.FromSeconds(-apiAheadSeconds));
        var fences = new GameplayFenceRepository(characters.ForCharacters(new WorldId(1)), database);
        _worlds.GameplayFences(new WorldId(1)).Returns(fences);
        _h.Sessions.TryRenewAsync(_head.AccountId, _head.GameSessionId, 2, "world-1", 0, 0,
            Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(call =>
            { _head.LeaseUntil = call.ArgAt<DateTime>(7); _head.LicenseUntil = call.ArgAt<DateTime>(8); return true; });

        await AssertHeldByTheGuard(await Activate());
        _h.Clock.Advance(GameAuthPolicy.HeartbeatInterval);
        await AssertHeldByTheGuard(await _service.HeartbeatAsync("world-1", _head.AccountId, _head.GameSessionId, 2, CancellationToken.None));

        async Task AssertHeldByTheGuard(GameSessionLeaseReply reply)
        {
            Assert.Null(reply.Error);
            AccountGameplayFence guard = (await fences.FindAsync(_head.AccountId, CancellationToken.None))!;
            Assert.Equal(GameplayFenceMode.Active, guard.Mode);
            Assert.Equal(guard.LeaseUntil, reply.LeaseUntil);
            Assert.True(reply.LeaseUntil <= database.GetUtcNow().UtcDateTime.Add(GameAuthPolicy.SessionLeaseLifetime));
            Assert.True(reply.LeaseUntil <= _h.Clock.GetUtcNow().UtcDateTime.Add(GameAuthPolicy.SessionLeaseLifetime));
        }
    }
    /// <summary>
    /// A retried activation whose target guard still pends but has run out (here the database's clock runs ahead of the
    /// API's, so the guard ends before the pending session does) is refused before the session activates: an active
    /// session with a guard that can never activate would hold the account until its lease ran out.
    /// </summary>
    [Fact]
    public async Task Leave_the_session_pending_when_a_retry_finds_its_target_guard_run_out()
    {
        await Arrange();
        using var characters = new SqliteWorlds(1);
        var database = new OffsetClock(_h.Clock, TimeSpan.FromSeconds(2));
        var fences = new GameplayFenceRepository(characters.ForCharacters(new WorldId(1)), database);
        _worlds.GameplayFences(new WorldId(1)).Returns(fences);
        // The first attempt's barrier landed, and its answer was lost.
        Assert.NotNull(await fences.AdvanceAsync(new(_head.AccountId, _head.GameSessionId, 2), false, _head.LeaseUntil, CancellationToken.None));
        _h.Clock.Advance(GameAuthPolicy.SessionLeaseLifetime - TimeSpan.FromSeconds(1));

        Assert.Equal(GameAuthErrors.BarrierPending, (await Activate()).Error);
        Assert.Equal(GameSessionState.Pending, _head.State);
        await _h.Sessions.DidNotReceive().TryActivateAsync(Arg.Any<Avalon.Common.ValueObjects.AccountId>(), Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }
    /// <summary>A clock a fixed offset from another, as the characters database's runs beside the API's.</summary>
    private sealed class OffsetClock(TimeProvider clock, TimeSpan offset) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => clock.GetUtcNow() + offset;
    }
    [Fact]
    public async Task Resume_the_same_pending_session_after_a_partial_cross_database_barrier_failure()
    {
        await Arrange();
        _target.AdvanceAsync(Arg.Any<GameplayWriteAuthority>(), false, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns((DateTime?)null);
        Assert.NotNull((await Activate()).Error);
        Assert.Equal(GameSessionState.Pending, _head.State);
        await _h.Sessions.DidNotReceive().TryActivateAsync(Arg.Any<Avalon.Common.ValueObjects.AccountId>(), Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        _target.AdvanceAsync(Arg.Any<GameplayWriteAuthority>(), false, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(call => call.ArgAt<DateTime>(2));
        Assert.Null((await Activate()).Error);
        Assert.Equal(2, _head.FencingToken);
    }
    [Fact]
    public async Task Reject_wrong_workload_stale_fence_and_authority_revoked_during_the_barrier()
    {
        await Arrange();
        Assert.NotNull((await Activate("world-2")).Error);
        Assert.NotNull((await _service.ActivateAsync("world-1", _head.AccountId, _head.GameSessionId, 1, CancellationToken.None)).Error);
        Assert.Empty(_order);
        _previous.AdvanceAsync(Arg.Any<GameplayWriteAuthority>(), true, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(call => { _h.Account.SessionEpoch++; return call.ArgAt<DateTime>(2); });
        Assert.NotNull((await Activate()).Error);
        await _h.Sessions.DidNotReceive().TryActivateAsync(Arg.Any<Avalon.Common.ValueObjects.AccountId>(), Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }
    [Fact]
    public async Task Refuse_gameplay_authority_when_sql_activated_but_the_target_guard_cannot_activate()
    {
        await Arrange();
        _target.ActivateAsync(Arg.Any<GameplayWriteAuthority>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns((DateTime?)null);
        Assert.NotNull((await Activate()).Error);
        Assert.Equal(GameSessionState.Active, _head.State);
        _target.ActivateAsync(Arg.Any<GameplayWriteAuthority>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(call => call.ArgAt<DateTime>(1));
        Assert.Null((await Activate()).Error);
    }
    [Fact]
    public async Task Clamp_heartbeat_to_current_ownership_and_refuse_a_revoked_context()
    {
        await Arrange();
        Assert.Null((await Activate()).Error);
        _h.Clock.Advance(TimeSpan.FromMinutes(4).Add(TimeSpan.FromSeconds(50)));
        _head.LeaseUntil = _h.Clock.GetUtcNow().UtcDateTime.AddSeconds(9);
        _h.Sessions.TryRenewAsync(_head.AccountId, _head.GameSessionId, 2, "world-1", 0, 0,
            Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(call =>
            { _head.LeaseUntil = call.ArgAt<DateTime>(7); _head.LicenseUntil = call.ArgAt<DateTime>(8); return true; });
        _target.RenewAsync(Arg.Any<GameplayWriteAuthority>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(call => call.ArgAt<DateTime>(1));
        GameSessionLeaseReply renewed = await _service.HeartbeatAsync("world-1", _head.AccountId, _head.GameSessionId, 2, CancellationToken.None);
        Assert.Null(renewed.Error);
        Assert.Equal(_h.Clock.GetUtcNow().UtcDateTime.AddSeconds(10), renewed.LeaseUntil);
        _h.Account.SessionEpoch++;
        Assert.NotNull((await _service.HeartbeatAsync("world-1", _head.AccountId, _head.GameSessionId, 2, CancellationToken.None)).Error);
    }
    /// <summary>
    /// #882: five wrong website passwords locked the row, and the next heartbeat ended the player's session. The lock
    /// guards the password steps only; a session proven otherwise keeps its lease.
    /// </summary>
    [Fact]
    public async Task Keep_a_session_whose_account_a_password_guesser_locked()
    {
        await Arrange();
        Assert.Null((await Activate()).Error);
        _h.Account.Locked = true;
        _h.Account.LockedUntil = _h.Clock.GetUtcNow().UtcDateTime.AddMinutes(15);
        _h.Sessions.TryRenewAsync(_head.AccountId, _head.GameSessionId, 2, "world-1", 0, 0,
            Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
        _target.RenewAsync(Arg.Any<GameplayWriteAuthority>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(call => call.ArgAt<DateTime>(1));

        GameSessionLeaseReply renewed = await _service.HeartbeatAsync("world-1", _head.AccountId, _head.GameSessionId, 2, CancellationToken.None);

        Assert.Null(renewed.Error);
        Assert.Equal("active", renewed.State);
    }

    [Fact]
    public async Task Never_publish_a_lease_when_the_character_database_cannot_renew_its_guard()
    {
        await Arrange();
        Assert.Null((await Activate()).Error);
        _h.Sessions.TryRenewAsync(Arg.Any<Avalon.Common.ValueObjects.AccountId>(), Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<long>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
        _target.RenewAsync(Arg.Any<GameplayWriteAuthority>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns((DateTime?)null);
        Assert.NotNull((await _service.HeartbeatAsync("world-1", _head.AccountId, _head.GameSessionId, 2, CancellationToken.None)).Error);
    }
    [Fact]
    public async Task Reject_an_old_session_end_after_replacement_without_ending_the_new_session()
    {
        await Arrange();
        Assert.NotNull((await _service.EndAsync("world-1", _head.AccountId, Guid.NewGuid(), 1, CancellationToken.None)).Error);
        await _target.DidNotReceive().EndAsync(Arg.Any<GameplayWriteAuthority>(), Arg.Any<CancellationToken>());
        await _h.Sessions.DidNotReceive().TryEndAsync(Arg.Any<Avalon.Common.ValueObjects.AccountId>(), Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        Assert.Equal(GameSessionState.Pending, _head.State);
    }
    /// <summary>
    /// #1013: a world whose admission failed after the redemption ends the pending session it reserved, so the player's
    /// next join is not refused for its lease. The activation may have failed before the target guard (which then holds
    /// no row, or the previous session's) or after advancing it: the session ends either way, the previous session's
    /// guard is left as it was, and a guard this session reached is blocked. An activation still in flight may land
    /// between the end's read of the guard and its write of the head: having only reached the guard ("raced-pending"),
    /// the session still expires and the guard is blocked after; having activated the head ("raced-active"), it ends as
    /// an active session, its guard blocked before the head ends. Only this world's workload can end it.
    /// </summary>
    [Theory]
    [InlineData("none")]
    [InlineData("previous")]
    [InlineData("advanced")]
    [InlineData("raced-pending")]
    [InlineData("raced-active")]
    public async Task End_a_pending_session_whose_activation_failed_wherever_it_stopped(string guardReached)
    {
        await Arrange();
        using var characters = new SqliteWorlds(1);
        var fences = new GameplayFenceRepository(characters.ForCharacters(new WorldId(1)), _h.Clock);
        DateTime until = _head.LeaseUntil;
        var self = new GameplayWriteAuthority(_head.AccountId, _head.GameSessionId, 2);
        _worlds.GameplayFences(new WorldId(1)).Returns(new ActivationLandsAfterRead(fences, async () =>
        {
            if (!guardReached.StartsWith("raced", StringComparison.Ordinal)) return;
            Assert.NotNull(await fences.AdvanceAsync(self, false, until, CancellationToken.None));
            if (guardReached != "raced-active") return;
            _head.State = GameSessionState.Active;
            Assert.NotNull(await fences.ActivateAsync(self, until, CancellationToken.None));
        }));
        var previous = new GameplayWriteAuthority(_head.AccountId, _head.PreviousGameSessionId!.Value, 1);
        if (guardReached == "previous")
        {
            Assert.NotNull(await fences.AdvanceAsync(previous, false, until, CancellationToken.None));
            Assert.NotNull(await fences.ActivateAsync(previous, until, CancellationToken.None));
        }
        else if (guardReached == "advanced")
        {
            Assert.NotNull(await fences.AdvanceAsync(self, false, until, CancellationToken.None));
        }
        // The Auth-DB head as the repository writes it: expiry only while pending, an end whatever the state.
        _h.Sessions.TryExpirePendingAsync(_head.AccountId, _head.GameSessionId, 2, Arg.Any<CancellationToken>())
            .Returns(_ => _head.State == GameSessionState.Pending);
        var guardWhenHeadEnded = new List<GameplayFenceMode>();
        _h.Sessions.TryEndAsync(_head.AccountId, _head.GameSessionId, 2, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            guardWhenHeadEnded.Add((await fences.FindAsync(_head.AccountId, CancellationToken.None))?.Mode ?? GameplayFenceMode.Blocked);
            return true;
        });

        Assert.NotNull((await _service.EndAsync("world-3", _head.AccountId, _head.GameSessionId, 2, CancellationToken.None)).Error);
        await _h.Sessions.DidNotReceive().TryEndAsync(Arg.Any<Avalon.Common.ValueObjects.AccountId>(), Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _h.Sessions.DidNotReceive().TryExpirePendingAsync(Arg.Any<Avalon.Common.ValueObjects.AccountId>(), Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<CancellationToken>());

        Assert.Equal("ended", (await _service.EndAsync("world-1", _head.AccountId, _head.GameSessionId, 2, CancellationToken.None)).State);
        // The head never ends while its guard could still let this session write.
        Assert.All(guardWhenHeadEnded, mode => Assert.Equal(GameplayFenceMode.Blocked, mode));
        Assert.Equal(guardReached is "advanced" or "raced-active", guardWhenHeadEnded.Count == 1);
        AccountGameplayFence? guard = await fences.FindAsync(_head.AccountId, CancellationToken.None);
        switch (guardReached)
        {
            case "previous":
                Assert.Equal((1L, GameplayFenceMode.Active), (guard!.FencingToken, guard.Mode));
                break;
            case "advanced" or "raced-pending" or "raced-active":
                Assert.Equal((2L, GameplayFenceMode.Blocked), (guard!.FencingToken, guard.Mode));
                break;
            default:
                Assert.True(guard is null || guard.FencingToken == 0);
                break;
        }
    }
    /// <summary>The real guard, with an activation that lands once a caller has read it.</summary>
    private sealed class ActivationLandsAfterRead(IGameplayFenceRepository inner, Func<Task> lands) : IGameplayFenceRepository
    {
        public Task<DateTime?> AdvanceAsync(GameplayWriteAuthority authority, bool blocked, DateTime leaseUntil, CancellationToken cancellationToken) => inner.AdvanceAsync(authority, blocked, leaseUntil, cancellationToken);
        public Task<DateTime?> ActivateAsync(GameplayWriteAuthority authority, DateTime leaseUntil, CancellationToken cancellationToken) => inner.ActivateAsync(authority, leaseUntil, cancellationToken);
        public Task<DateTime?> RenewAsync(GameplayWriteAuthority authority, DateTime leaseUntil, CancellationToken cancellationToken) => inner.RenewAsync(authority, leaseUntil, cancellationToken);
        public Task<bool> EndAsync(GameplayWriteAuthority authority, CancellationToken cancellationToken) => inner.EndAsync(authority, cancellationToken);
        public async Task<AccountGameplayFence?> FindAsync(Avalon.Common.ValueObjects.AccountId accountId, CancellationToken cancellationToken)
        {
            AccountGameplayFence? read = await inner.FindAsync(accountId, cancellationToken);
            await lands();
            return read;
        }
    }
    [Fact]
    public async Task Apply_a_single_target_barrier_for_same_world_takeover()
    {
        await Arrange();
        _head.PreviousWorldId = 1;
        Assert.Null((await Activate()).Error);
        Assert.Equal(new[] { "target", "sql-active", "target-active" }, _order);
        await _previous.DidNotReceive().AdvanceAsync(Arg.Any<GameplayWriteAuthority>(), Arg.Any<bool>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }
    [Fact]
    public async Task End_after_context_revocation_only_after_blocking_its_durable_save_guard()
    {
        await Arrange();
        Assert.Null((await Activate()).Error);
        _h.Account.SessionEpoch++;
        _order.Clear();
        _target.EndAsync(Arg.Any<GameplayWriteAuthority>(), Arg.Any<CancellationToken>()).Returns(_ => { _order.Add("guard-ended"); return true; });
        _h.Sessions.TryEndAsync(_head.AccountId, _head.GameSessionId, 2, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(_ => { _order.Add("sql-ended"); return true; });
        GameSessionLeaseReply reply = await _service.EndAsync("world-1", _head.AccountId, _head.GameSessionId, 2, CancellationToken.None);
        Assert.Null(reply.Error);
        Assert.Equal("ended", reply.State);
        Assert.Equal(new[] { "guard-ended", "sql-ended" }, _order);
    }
    [Fact]
    public async Task Leave_the_sql_session_live_when_its_end_barrier_is_unavailable_and_allow_retry()
    {
        await Arrange();
        Assert.Null((await Activate()).Error);
        _target.EndAsync(Arg.Any<GameplayWriteAuthority>(), Arg.Any<CancellationToken>()).Returns(false);
        Assert.Equal("BARRIER_PENDING", (await _service.EndAsync("world-1", _head.AccountId, _head.GameSessionId, 2, CancellationToken.None)).Error);
        await _h.Sessions.DidNotReceive().TryEndAsync(Arg.Any<Avalon.Common.ValueObjects.AccountId>(), Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        _target.EndAsync(Arg.Any<GameplayWriteAuthority>(), Arg.Any<CancellationToken>()).Returns(true);
        _h.Sessions.TryEndAsync(_head.AccountId, _head.GameSessionId, 2, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
        Assert.Equal("ended", (await _service.EndAsync("world-1", _head.AccountId, _head.GameSessionId, 2, CancellationToken.None)).State);
    }

    /// <summary>#967: the place follows the session: activation renews it and frees the other world's, heartbeats renew it, the end removes it.</summary>
    [Fact]
    public async Task Keep_the_place_with_the_session_and_remove_it_at_its_end()
    {
        await Arrange();
        DateTime now = _h.Clock.GetUtcNow().UtcDateTime;
        await _h.Places.RenewAsync(2, 7, 1, Guid.NewGuid(), now.AddSeconds(45), false, default); // an older session's place left in world 2
        Assert.Null((await Activate()).Error);
        Assert.Equal((1, 0), ((await _h.Places.CountAsync(1, default)).Taken, (await _h.Places.CountAsync(2, default)).Taken));

        RenewAs(_head);
        _h.Clock.Advance(TimeSpan.FromSeconds(40));
        Assert.Null((await _service.HeartbeatAsync("world-1", _head.AccountId, _head.GameSessionId, 2, default)).Error);
        _h.Clock.Advance(TimeSpan.FromSeconds(10)); // past the activation's lease: only the heartbeat keeps the place
        Assert.Equal(1, (await _h.Places.CountAsync(1, default)).Taken);

        _target.EndAsync(Arg.Any<GameplayWriteAuthority>(), Arg.Any<CancellationToken>()).Returns(true);
        _h.Sessions.TryEndAsync(_head.AccountId, _head.GameSessionId, 2, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
        Assert.Equal("ended", (await _service.EndAsync("world-1", _head.AccountId, _head.GameSessionId, 2, default)).State);
        Assert.Equal(0, (await _h.Places.CountAsync(1, default)).Taken);
    }

    /// <summary>Review Focus 1: the ledger unreachable never costs a session; the first renewal that reaches it puts the place back.</summary>
    [Fact]
    public async Task Keep_the_session_when_the_place_ledger_is_unreachable_and_put_the_place_back()
    {
        var ledger = new FlakyPlaceStore(new MemoryWorldPlaceStore(_h.Clock, new WorldPlacesConfiguration())) { Down = true };
        await Arrange(places: ledger);
        Assert.Null((await Activate()).Error);
        RenewAs(_head);
        Assert.Null((await _service.HeartbeatAsync("world-1", _head.AccountId, _head.GameSessionId, 2, default)).Error);
        Assert.Equal(0, (await ledger.Inner.CountAsync(1, default)).Taken);

        ledger.Down = false;
        Assert.Null((await _service.HeartbeatAsync("world-1", _head.AccountId, _head.GameSessionId, 2, default)).Error);
        Assert.Equal(1, (await ledger.Inner.CountAsync(1, default)).Taken);
    }

    /// <summary>Heartbeats succeed and move the head's lease as the repository would.</summary>
    private void RenewAs(GameSession head)
    {
        _h.Sessions.TryRenewAsync(head.AccountId, head.GameSessionId, 2, "world-1", 0, 0,
            Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(call =>
            { head.LeaseUntil = call.ArgAt<DateTime>(7); head.LicenseUntil = call.ArgAt<DateTime>(8); return true; });
        // The guard answers the end it holds, capped on its own clock (#1009).
        _target.RenewAsync(Arg.Any<GameplayWriteAuthority>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<DateTime>(1));
    }
}

/// <summary>A place store that fails as Redis does when <see cref="Down"/>, and otherwise is <see cref="Inner"/>.</summary>
internal sealed class FlakyPlaceStore(IWorldPlaceStore inner) : IWorldPlaceStore
{
    public IWorldPlaceStore Inner => inner;
    public bool Down { get; set; }
    private T Up<T>(T value) => Down ? throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, CommandFlags.CommandRetryNever, "down") : value;
    public Task<PlaceDecision> EnterAsync(PlaceRequest request, CancellationToken ct) => Up(inner).EnterAsync(request, ct);
    public Task RenewAsync(ushort worldId, long accountId, long fence, Guid sessionId, DateTime until, bool activation,
        CancellationToken ct) => Up(inner).RenewAsync(worldId, accountId, fence, sessionId, until, activation, ct);
    public Task EndAsync(ushort worldId, long accountId, long fence, PlaceEnd end, DateTime heldUntil, CancellationToken ct) =>
        Up(inner).EndAsync(worldId, accountId, fence, end, heldUntil, ct);
    public Task WithdrawAsync(ushort worldId, long accountId, Guid ticket, DateTime until, CancellationToken ct) =>
        Up(inner).WithdrawAsync(worldId, accountId, ticket, until, ct);
    public Task<QueueCheckIn> CheckInAsync(ushort worldId, long accountId, int? limit, bool available, CancellationToken ct) =>
        Up(inner).CheckInAsync(worldId, accountId, limit, available, ct);
    public Task<bool> LeaveAsync(ushort worldId, long accountId, CancellationToken ct) => Up(inner).LeaveAsync(worldId, accountId, ct);
    public Task<WorldPlaceCounts> CountAsync(ushort worldId, CancellationToken ct) => Up(inner).CountAsync(worldId, ct);
}
