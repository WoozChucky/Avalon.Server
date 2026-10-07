using Avalon.Api.Services;
using Avalon.Api.Worlds;
using Avalon.Common.GameAuth;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.GameAuth;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.GameAuth;

public sealed class GameSessionFenceServiceShould
{
    private readonly JoinHarness _h = new();
    private readonly IWorldRepositories _worlds = Substitute.For<IWorldRepositories>();
    private readonly IGameplayFenceRepository _previous = Substitute.For<IGameplayFenceRepository>();
    private readonly IGameplayFenceRepository _target = Substitute.For<IGameplayFenceRepository>();
    private readonly List<string> _order = [];
    private GameSession _head = null!;
    private GameSessionFenceService _service = null!;
    private async Task Arrange(uint? appId = null, ushort worldId = 1, bool native = false)
    {
        var auth = native ? await _h.AuthenticateAvalon() : await _h.Authenticate(appId);
        var context = (await _h.Authorization.GetContextAsync(auth.GameContextCredential!, true, CancellationToken.None))!;
        var now = _h.Clock.GetUtcNow().UtcDateTime;
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
        _previous.AdvanceAsync(Arg.Any<GameplayWriteAuthority>(), true, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(_ => { _order.Add("previous"); return true; });
        _target.AdvanceAsync(Arg.Any<GameplayWriteAuthority>(), false, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(_ => { _order.Add("target"); return true; });
        _h.Sessions.TryActivateAsync(_head.AccountId, _head.GameSessionId, 2, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(call =>
        { _order.Add("sql-active"); _head.State = GameSessionState.Active; _head.LeaseUntil = call.ArgAt<DateTime>(4); return true; });
        _target.ActivateAsync(Arg.Any<GameplayWriteAuthority>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(_ => { _order.Add("target-active"); return true; });
        var accounts = Substitute.For<IAccountRepository>();
        accounts.FindByIdAsync(_h.Account.Id, false, Arg.Any<CancellationToken>()).Returns(_h.Account);
        _service = new(_h.Sessions, _h.Authorization, _worlds, accounts, Options.Create(new GameWorkloadConfiguration { Servers = [new GameServerDefinition { ServerId = "world-1", WorldId = 1 }, new GameServerDefinition { ServerId = "world-2", WorldId = 2 }, new GameServerDefinition { ServerId = "world-3", WorldId = 3 }] }), _h.Clock, new GameApplicationAccessPolicy(Options.Create(_h.Configuration)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Activation_and_heartbeat_recheck_the_actual_world_application_policy(ushort worldId)
    {
        await Arrange(2514590, worldId);
        _h.Account.AccessLevel |= Avalon.Common.Accounts.AccountAccessLevel.Admin;
        var first = await Activate("world-" + worldId);
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shared_license_revocation_refuses_heartbeat_without_extending_the_lease(bool native)
    {
        await Arrange(native: native);
        Assert.Null((await Activate()).Error);
        var until = _head.LeaseUntil;
        _h.RevokeLicense();
        Assert.Equal(GameAuthErrors.SessionRevoked, (await _service.HeartbeatAsync("world-1", _head.AccountId, _head.GameSessionId, 2, default)).Error);
        Assert.Equal(until, _head.LeaseUntil);
        await _h.Sessions.DidNotReceive().TryRenewAsync(Arg.Any<Avalon.Common.ValueObjects.AccountId>(), Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<long>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _target.DidNotReceive().RenewAsync(Arg.Any<GameplayWriteAuthority>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }
    [Fact]
    public async Task Advance_both_character_database_barriers_before_publishing_active_authority()
    {
        await Arrange();
        var reply = await Activate();
        Assert.Null(reply.Error);
        Assert.Equal("active", reply.State);
        Assert.Equal(new[] { "previous", "target", "sql-active", "target-active" }, _order);
        Assert.Equal(_h.Clock.GetUtcNow().UtcDateTime.AddSeconds(45), reply.LeaseUntil);
    }
    [Fact]
    public async Task Resume_the_same_pending_session_after_a_partial_cross_database_barrier_failure()
    {
        await Arrange();
        _target.AdvanceAsync(Arg.Any<GameplayWriteAuthority>(), false, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(false);
        Assert.NotNull((await Activate()).Error);
        Assert.Equal(GameSessionState.Pending, _head.State);
        await _h.Sessions.DidNotReceive().TryActivateAsync(Arg.Any<Avalon.Common.ValueObjects.AccountId>(), Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        _target.AdvanceAsync(Arg.Any<GameplayWriteAuthority>(), false, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
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
        _previous.AdvanceAsync(Arg.Any<GameplayWriteAuthority>(), true, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(_ => { _h.Account.SessionEpoch++; return true; });
        Assert.NotNull((await Activate()).Error);
        await _h.Sessions.DidNotReceive().TryActivateAsync(Arg.Any<Avalon.Common.ValueObjects.AccountId>(), Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }
    [Fact]
    public async Task Refuse_gameplay_authority_when_sql_activated_but_the_target_guard_cannot_activate()
    {
        await Arrange();
        _target.ActivateAsync(Arg.Any<GameplayWriteAuthority>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(false);
        Assert.NotNull((await Activate()).Error);
        Assert.Equal(GameSessionState.Active, _head.State);
        _target.ActivateAsync(Arg.Any<GameplayWriteAuthority>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
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
        _target.RenewAsync(Arg.Any<GameplayWriteAuthority>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
        var renewed = await _service.HeartbeatAsync("world-1", _head.AccountId, _head.GameSessionId, 2, CancellationToken.None);
        Assert.Null(renewed.Error);
        Assert.Equal(_h.Clock.GetUtcNow().UtcDateTime.AddSeconds(10), renewed.LeaseUntil);
        _h.Account.SessionEpoch++;
        Assert.NotNull((await _service.HeartbeatAsync("world-1", _head.AccountId, _head.GameSessionId, 2, CancellationToken.None)).Error);
    }
    [Fact]
    public async Task Never_publish_a_lease_when_the_character_database_cannot_renew_its_guard()
    {
        await Arrange();
        Assert.Null((await Activate()).Error);
        _h.Sessions.TryRenewAsync(Arg.Any<Avalon.Common.ValueObjects.AccountId>(), Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<long>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
        _target.RenewAsync(Arg.Any<GameplayWriteAuthority>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(false);
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
        var reply = await _service.EndAsync("world-1", _head.AccountId, _head.GameSessionId, 2, CancellationToken.None);
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
}
