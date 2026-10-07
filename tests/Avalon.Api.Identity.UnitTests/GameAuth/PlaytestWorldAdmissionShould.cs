using Avalon.Api.Hosting.Worlds;
using Avalon.Api.Identity.Controllers;
using Avalon.Api.Identity.Services;
using Avalon.Api.Services;
using Avalon.Common.Accounts;
using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.GameAuth;
using Avalon.Infrastructure.StoreAuth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;
using GameContextCredentialRequest = Avalon.Api.Contract.GameContextCredentialRequest;

namespace Avalon.Api.Identity.UnitTests.GameAuth;

public sealed class PlaytestWorldAdmissionShould
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Admit_and_reconnect_a_linked_or_new_Player_to_a_PTR_world_without_promoting_the_account(bool createAccount)
    {
        var h = new Harness(createAccount);
        GameAuthReply auth = await h.Authenticate(2514590);
        GameContextRecord context = Assert.IsType<GameContextRecord>(await h.Authorization.GetContextAsync(auth.GameContextCredential!, true, default));
        Assert.Equal((ushort)3, Assert.Single(await h.Allocator.ListAsync(context, default)).WorldId);
        OkObjectResult listed = Assert.IsType<OkObjectResult>(await h.Worlds(auth.GameContextCredential!));
        Assert.Equal((ushort)3, Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<GameWorldDestination>>(listed.Value)).WorldId);
        Assert.Equal(AccountAccessLevel.Player, h.Account.AccessLevel);

        GameJoinReply issued = await h.Tickets.IssueAsync(auth.GameContextCredential!, 3, null, Guid.NewGuid(), false, false, default);
        Assert.Null(issued.Error);
        JoinRedemptionReceipt admitted = await h.Tickets.RedeemAsync(issued.JoinTicket!, "world-3", Guid.NewGuid(), Guid.NewGuid(), default);
        Assert.Null(admitted.Error);
        Assert.Equal(context.Id.ToString("D"), admitted.GameContextId);
        Assert.Equal("1", admitted.FencingToken);
        GameSessionLeaseReply lease = await h.Activate();
        Assert.Null(lease.Error);
        Assert.Equal((ushort?)1, lease.AccessLevel);
        Assert.Equal((ushort?)3, lease.WorldId);
        Assert.Equal((ushort?)1, (await h.Heartbeat()).AccessLevel);

        GameJoinReply reconnect = await h.Tickets.IssueAsync(auth.GameContextCredential!, 3, null, Guid.NewGuid(), false, true, default);
        Assert.Null(reconnect.Error);
        JoinRedemptionReceipt rejoined = await h.Tickets.RedeemAsync(reconnect.JoinTicket!, "world-3", Guid.NewGuid(), Guid.NewGuid(), default);
        Assert.Null(rejoined.Error);
        Assert.Equal(context.Id.ToString("D"), rejoined.GameContextId);
        Assert.Equal("2", rejoined.FencingToken);
        Assert.NotEqual(admitted.GameSessionId, rejoined.GameSessionId);
        Assert.Equal((ushort?)1, (await h.Activate()).AccessLevel);
        Assert.Equal(AccountAccessLevel.Player, h.Account.AccessLevel);
    }

    [Theory]
    [InlineData(2514590u, false)]
    [InlineData(2514590u, true)]
    [InlineData(2499460u, false)]
    [InlineData(2499460u, true)]
    public async Task Require_current_Playtest_ownership_to_list_PTR_without_changing_main_context_listing(uint appId, bool expired)
    {
        var h = new Harness(false) { OwnsApplication = expired, LicenseLifetime = TimeSpan.FromSeconds(30) };
        if (appId == 2499460) h.Account.AccessLevel |= AccountAccessLevel.PTR;
        GameAuthReply auth = await h.Authenticate(appId, expired ? GameAuthStates.Authorized : GameAuthStates.PendingLicense);
        if (expired) h.Advance(TimeSpan.FromSeconds(31));
        Assert.NotNull(await h.Authorization.GetContextAsync(auth.GameContextCredential!, false, default));
        Assert.Null(await h.Authorization.GetContextAsync(auth.GameContextCredential!, true, default));

        IActionResult result = await h.Worlds(auth.GameContextCredential!);
        if (appId == 2514590)
        {
            Assert.IsType<UnauthorizedObjectResult>(result);
        }
        else
        {
            OkObjectResult listed = Assert.IsType<OkObjectResult>(result);
            Assert.Equal((ushort)3, Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<GameWorldDestination>>(listed.Value)).WorldId);
        }
    }

    [Fact]
    public async Task Keep_the_same_Players_main_application_context_out_of_PTR()
    {
        var h = new Harness(false);
        GameAuthReply playtest = await h.Authenticate(2514590);
        GameContextRecord playtestContext = Assert.IsType<GameContextRecord>(await h.Authorization.GetContextAsync(playtest.GameContextCredential!, true, default));
        Assert.Equal((ushort)3, Assert.Single(await h.Allocator.ListAsync(playtestContext, default)).WorldId);

        GameAuthReply main = await h.Authenticate(2499460);
        GameContextRecord mainContext = Assert.IsType<GameContextRecord>(await h.Authorization.GetContextAsync(main.GameContextCredential!, true, default));
        Assert.Equal(playtestContext.AccountId, mainContext.AccountId);
        Assert.Empty(await h.Allocator.ListAsync(mainContext, default));
        GameJoinReply denied = await h.Tickets.IssueAsync(main.GameContextCredential!, 3, null, Guid.NewGuid(), false, false, default);
        Assert.Equal(GameAuthErrors.WorldUnavailable, denied.Error);
        Assert.Equal(AccountAccessLevel.Player, h.Account.AccessLevel);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Refuse_fresh_and_cached_admission_and_heartbeat_when_Playtest_is_disabled_or_ownership_is_lost(bool disableApplication)
    {
        var h = new Harness(false);
        GameAuthReply auth = await h.Authenticate(2514590);
        var request = Guid.NewGuid();
        GameJoinReply issued = await h.Tickets.IssueAsync(auth.GameContextCredential!, 3, null, request, false, false, default);
        Assert.Null(issued.Error);
        var connection = Guid.NewGuid();
        var redemption = Guid.NewGuid();
        JoinRedemptionReceipt admitted = await h.Tickets.RedeemAsync(issued.JoinTicket!, "world-3", connection, redemption, default);
        Assert.Null(admitted.Error);
        Assert.Null((await h.Activate()).Error);
        Assert.Null((await h.Heartbeat()).Error);
        GameJoinReply reconnect = await h.Tickets.IssueAsync(auth.GameContextCredential!, 3, null, Guid.NewGuid(), false, true, default);
        Assert.Null(reconnect.Error);

        if (disableApplication) h.Configuration.SteamPlaytest.Enabled = false;
        else await h.RevokePlaytestAuthority();

        Assert.NotNull((await h.Tickets.IssueAsync(auth.GameContextCredential!, 3, null, request, false, false, default)).Error);
        Assert.NotNull((await h.Tickets.IssueAsync(auth.GameContextCredential!, 3, null, Guid.NewGuid(), false, true, default)).Error);
        Assert.NotNull((await h.Tickets.RedeemAsync(issued.JoinTicket!, "world-3", connection, redemption, default)).Error);
        Assert.NotNull((await h.Tickets.RedeemAsync(reconnect.JoinTicket!, "world-3", Guid.NewGuid(), Guid.NewGuid(), default)).Error);
        Assert.NotNull((await h.Heartbeat()).Error);
        Assert.NotNull((await h.Activate()).Error);
        Assert.Equal(AccountAccessLevel.Player, h.Account.AccessLevel);
    }

    [Fact]
    public async Task Suspension_refuses_fresh_Steam_proof_cached_admission_and_session_renewal()
    {
        var h = new Harness(false);
        GameAuthReply auth = await h.Authenticate(2514590);
        var request = Guid.NewGuid();
        GameJoinReply issued = await h.Tickets.IssueAsync(auth.GameContextCredential!, 3, null, request, false, false, default);
        Assert.Null(issued.Error);
        var connection = Guid.NewGuid();
        var redemption = Guid.NewGuid();
        Assert.Null((await h.Tickets.RedeemAsync(issued.JoinTicket!, "world-3", connection, redemption, default)).Error);
        Assert.Null((await h.Activate()).Error);
        Assert.Null((await h.Heartbeat()).Error);
        GameJoinReply reconnect = await h.Tickets.IssueAsync(auth.GameContextCredential!, 3, null, Guid.NewGuid(), false, true, default);
        Assert.Null(reconnect.Error);

        h.SetPlaytestSuspension(true);
        await h.Authenticate(2514590, GameAuthStates.PendingLicense);
        Assert.NotNull((await h.Tickets.IssueAsync(auth.GameContextCredential!, 3, null, request, false, false, default)).Error);
        Assert.NotNull((await h.Tickets.IssueAsync(auth.GameContextCredential!, 3, null, Guid.NewGuid(), false, true, default)).Error);
        Assert.NotNull((await h.Tickets.RedeemAsync(issued.JoinTicket!, "world-3", connection, redemption, default)).Error);
        Assert.NotNull((await h.Tickets.RedeemAsync(reconnect.JoinTicket!, "world-3", Guid.NewGuid(), Guid.NewGuid(), default)).Error);
        Assert.NotNull((await h.Heartbeat()).Error);
        Assert.NotNull((await h.Activate()).Error);

        h.SetPlaytestSuspension(false);
        Assert.Null(await h.Authorization.GetContextAsync(auth.GameContextCredential!, true, default));
        GameAuthReply fresh = await h.Authenticate(2514590);
        Assert.NotNull(await h.Authorization.GetContextAsync(fresh.GameContextCredential!, true, default));
        Assert.Equal(AccountAccessLevel.Player, h.Account.AccessLevel);
    }

    private sealed class Harness
    {
        private const string Subject = "76561198000000001";
        private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));
        private readonly GameSessionFenceService _fences;
        private GameSession? _head;
        private int _proof;

        public Account Account { get; }
        public StoreAuthenticationConfiguration Configuration { get; } = new()
        {
            SteamAppId = 2499460,
            SteamPublisherKey = "test-only",
            SteamPlaytest = new() { Enabled = true, AppId = 2514590, AllowedWorldIds = [3] },
        };
        private MemoryGameLicenses _licenses = null!;
        public void SetPlaytestSuspension(bool active)
        {
            foreach (GameLicense? license in _licenses.Rows.Where(x => x.ProviderProductId == "2514590"))
            {
                license.SuspendedAt = active ? Now : null;
                license.AuthorityRevision++;
            }
        }
        public async Task RevokePlaytestAuthority()
        {
            foreach (GameLicense? license in _licenses.Rows.Where(x => x.ProviderProductId == "2514590"))
                await _licenses.ApplyDecisionAsync(license.Id, license.AuthorityRevision, new(false, Now, Now));
        }
        public bool OwnsApplication { get; set; } = true;
        public TimeSpan LicenseLifetime { get; set; } = TimeSpan.FromMinutes(5);
        public GameAuthorizationService Authorization { get; }
        public GameServerAllocator Allocator { get; }
        public JoinTicketStore Tickets { get; }
        private DateTime Now => _clock.GetUtcNow().UtcDateTime;

        public Harness(bool createAccount)
        {
            Account = new Account
            {
                Id = new AccountId(7),
                Username = "PLAYER",
                Email = createAccount ? null : "player@example.test",
                Salt = createAccount ? [] : [1],
                Verifier = createAccount ? [] : [2],
                IsStoreGenerated = createAccount,
                JoinDate = DateTime.UnixEpoch,
                AccessLevel = AccountAccessLevel.Player,
                Status = AccountStatus.Active,
                SessionEpoch = 1,
            };
            var identity = new ExternalIdentity { Id = Guid.NewGuid(), AccountId = Account.Id, Provider = "steam", ProviderSubject = Subject };
            ExternalIdentity? linked = createAccount ? null : identity;
            IAccountRepository accounts = Substitute.For<IAccountRepository>();
            accounts.FindByIdAsync(Account.Id, false, Arg.Any<CancellationToken>()).Returns(_ => linked is null ? null : Account);
            IExternalIdentityRepository identities = Substitute.For<IExternalIdentityRepository>();
            identities.FindAsync("steam", Subject, Arg.Any<CancellationToken>()).Returns(_ => linked);
            IGameAccountRegistration registration = Substitute.For<IGameAccountRegistration>();
            registration.CreateFromStoreAsync(Arg.Any<Guid>(), "steam", Subject, Arg.Any<DateTime>(), "127.0.0.1", Arg.Any<CancellationToken>())
                .Returns(_ => { linked = identity; return new IdentityLinkResult(IdentityLinkStatus.Linked, identity); });
            ISteamProofVerifier proof = Substitute.For<ISteamProofVerifier>();
            proof.VerifyAsync(Arg.Any<uint>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new SteamProofResult(SteamProofStatus.Verified, Subject));
            ISteamOwnershipClient ownership = Substitute.For<ISteamOwnershipClient>();
            ownership.CheckAsync(Arg.Any<uint>(), Subject, Arg.Any<CancellationToken>())
                .Returns(_ => new SteamOwnershipResult(OwnsApplication ? SteamOwnershipStatus.Owned : SteamOwnershipStatus.NotOwned,
                    Subject, Now, OwnsApplication ? Now.Add(LicenseLifetime) : Now));
            ILicenseObservationRepository observations = Substitute.For<ILicenseObservationRepository>();
            var store = new AtomicAuthStore();
            _licenses = TestGameAuthorization.Licenses(store);
            var crypto = new GameAuthCryptography(Enumerable.Range(1, 32).Select(x => (byte)x).ToArray());
            IOptions<StoreAuthenticationConfiguration> options = Options.Create(Configuration);
            var applications = new GameApplicationAccessPolicy(options);
            Authorization = TestGameAuthorization.Create(store, new AuthAttemptStore(store, crypto, options, _clock), crypto, accounts,
                Substitute.For<IRefreshTokenRepository>(), identities, observations, proof, ownership, options, _clock, registration);

            var world = new Avalon.Domain.Auth.World
            {
                Id = new WorldId(3),
                Name = "PTR",
                AccessLevelRequired = AccountAccessLevel.PTR,
                Host = "ptr.example.test",
                Port = 21000,
                MinVersion = "0.2.0",
                Version = "0.2.0",
            };
            IWorldRepository worlds = Substitute.For<IWorldRepository>();
            worlds.FindByIdAsync(world.Id, false, Arg.Any<CancellationToken>()).Returns(world);
            IWorldDatabases databases = Substitute.For<IWorldDatabases>();
            databases.IsAvailable(world.Id).Returns(true);
            IWorldReadiness readiness = Substitute.For<IWorldReadiness>();
            readiness.IsReadyAsync(3, Arg.Any<CancellationToken>()).Returns(true);
            IWorldRepositories repositories = Substitute.For<IWorldRepositories>();
            IOptions<GameWorkloadConfiguration> workloads = Options.Create(new GameWorkloadConfiguration
            {
                Servers = [new GameServerDefinition { ServerId = "world-3", WorldId = 3, TlsServerName = "ptr.example.test", TlsCertificateSha256 = new string('A', 64) }],
            });
            Allocator = new(worlds, accounts, databases, readiness, repositories, workloads, _clock, applications);

            IGameSessionRepository sessions = Substitute.For<IGameSessionRepository>();
            sessions.FindAsync(Account.Id, Arg.Any<CancellationToken>()).Returns(_ => _head);
            sessions.TryReserveAsync(Arg.Any<GameSessionReservation>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                GameSessionReservation reservation = call.Arg<GameSessionReservation>();
                if (reservation.ExpectedFence != (_head?.FencingToken ?? 0)) return null;
                _head = new GameSession
                {
                    AccountId = reservation.AccountId,
                    GameSessionId = reservation.GameSessionId,
                    GameContextId = reservation.GameContextId,
                    FencingToken = reservation.ExpectedFence + 1,
                    ServerId = reservation.ServerId,
                    WorldId = reservation.WorldId,
                    Environment = reservation.Environment,
                    CredentialsVersion = reservation.CredentialsVersion,
                    SessionEpoch = reservation.SessionEpoch,
                    State = GameSessionState.Pending,
                    CreatedAt = Now,
                    LeaseUntil = Now.AddSeconds(45),
                    LicenseUntil = reservation.LicenseUntil,
                };
                return _head;
            });
            sessions.TryActivateAsync(Account.Id, Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                if (_head is null || _head.GameSessionId != call.Arg<Guid>() || _head.FencingToken != call.Arg<long>()) return false;
                _head.State = GameSessionState.Active;
                _head.LeaseUntil = call.ArgAt<DateTime>(4);
                return true;
            });
            sessions.TryRenewAsync(Account.Id, Arg.Any<Guid>(), Arg.Any<long>(), "world-3", Arg.Any<int>(), Arg.Any<long>(),
                Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                if (_head is null || _head.GameSessionId != call.Arg<Guid>() || _head.FencingToken != call.ArgAt<long>(2)) return false;
                _head.LeaseUntil = call.ArgAt<DateTime>(7);
                _head.LicenseUntil = call.ArgAt<DateTime>(8);
                return true;
            });
            IGameplayFenceRepository gameplay = Substitute.For<IGameplayFenceRepository>();
            repositories.GameplayFences(world.Id).Returns(gameplay);
            gameplay.AdvanceAsync(Arg.Any<GameplayWriteAuthority>(), false, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
            gameplay.ActivateAsync(Arg.Any<GameplayWriteAuthority>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
            gameplay.RenewAsync(Arg.Any<GameplayWriteAuthority>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
            Tickets = new(store, crypto, Authorization, sessions, Allocator, options, _clock, applications);
            _fences = new(sessions, Authorization, repositories, accounts, workloads, _clock, applications);
        }

        public async Task<GameAuthReply> Authenticate(uint appId, string expectedState = GameAuthStates.Authorized)
        {
            AuthAttemptReply attempt = (await Authorization.CreateAttemptAsync("steam", GameWorkloadConfiguration.ClientProtocolVersion,
                Guid.NewGuid(), new string('A', 43), null, appId, default))!;
            GameAuthReply reply = await Authorization.AuthenticateSteamAsync(attempt.AttemptCredential,
                (++_proof).ToString("X4", System.Globalization.CultureInfo.InvariantCulture), Guid.NewGuid(), default, "127.0.0.1");
            Assert.Null(reply.Error);
            Assert.Equal(expectedState, reply.State);
            return reply;
        }

        public void Advance(TimeSpan elapsed) => _clock.Advance(elapsed);
        public Task<IActionResult> Worlds(string credential)
        {
            var controller = new GameAdmissionController(Authorization, Tickets, Allocator, new GameApplicationAccessPolicy(Options.Create(Configuration)))
            { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
            controller.Request.Scheme = "https";
            return controller.Worlds(new GameContextCredentialRequest { GameContextCredential = credential }, default);
        }

        public Task<GameSessionLeaseReply> Activate() => _fences.ActivateAsync("world-3", Account.Id, _head!.GameSessionId, _head.FencingToken, default);
        public Task<GameSessionLeaseReply> Heartbeat() => _fences.HeartbeatAsync("world-3", Account.Id, _head!.GameSessionId, _head.FencingToken, default);
    }
}
