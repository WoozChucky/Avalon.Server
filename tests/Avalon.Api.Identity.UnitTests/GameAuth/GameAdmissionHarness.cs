using System.Security.Claims;
using Avalon.Api.Hosting.Worlds;
using Avalon.Api.Identity.Authentication;
using Avalon.Api.Identity.Controllers;
using Avalon.Api.Identity.Services;
using Avalon.Api.Services;
using Avalon.Api.Testing;
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
using Avalon.Infrastructure.WorldPlaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;
using GameContextCredentialRequest = Avalon.Api.Contract.GameContextCredentialRequest;
using GameJoinRequest = Avalon.Api.Contract.GameJoinRequest;
using JoinRedemptionRequest = Avalon.Api.Contract.JoinRedemptionRequest;

namespace Avalon.Api.Identity.UnitTests.GameAuth;

/// <summary>
/// The game admission through its real services (authorization, the allocator, the ticket store, the controller), over
/// substituted repositories: one PTR world, 3, on workload <c>world-3</c>.
/// </summary>
internal sealed class GameAdmissionHarness
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
    /// <summary>The place ledger (#967) the ticket store and the fence service share.</summary>
    public IWorldPlaceStore Places { get; }
    /// <summary>World 3's auth row, as the allocator reads it.</summary>
    public Avalon.Domain.Auth.World World { get; }
    public IWorldReadiness Readiness { get; } = Substitute.For<IWorldReadiness>();
    /// <summary>The auth world rows: world 3 only, until a test adds one.</summary>
    public IWorldRepository WorldRows { get; } = Substitute.For<IWorldRepository>();
    public IWorldDatabases Databases { get; } = Substitute.For<IWorldDatabases>();
    /// <summary>The request of the last <see cref="Join"/>, whose response headers it carries.</summary>
    public HttpContext? LastJoin { get; private set; }
    public GameWorkloadConfiguration Workloads { get; } = new()
    {
        MinClientVersion = GameClientTestData.MinClientVersion,
        Servers = [new GameServerDefinition { ServerId = "world-3", WorldId = 3, TlsServerName = "ptr.example.test", TlsCertificateSha256 = new string('A', 64) }],
    };
    /// <summary>World 3's characters: none until a test adds one.</summary>
    public ICharacterRepository Characters { get; } = Substitute.For<ICharacterRepository>();
    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public GameAdmissionHarness(bool createAccount)
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

        Avalon.Domain.Auth.World world = World = new()
        {
            Id = new WorldId(3),
            Name = "PTR",
            AccessLevelRequired = AccountAccessLevel.PTR,
            Host = "ptr.example.test",
            Port = 21000,
            MinVersion = "0.2.0",
            Version = "0.2.0",
        };
        WorldRows.FindByIdAsync(world.Id, false, Arg.Any<CancellationToken>()).Returns(world);
        Databases.IsAvailable(world.Id).Returns(true);
        Readiness.IsReadyAsync(3, Arg.Any<CancellationToken>()).Returns(true);
        IWorldRepositories repositories = Substitute.For<IWorldRepositories>();
        repositories.Characters(world.Id).Returns(Characters);
        IOptions<GameWorkloadConfiguration> workloads = Options.Create(Workloads);
        Allocator = new(WorldRows, accounts, Databases, Readiness, repositories, workloads, _clock, applications, NullLogger<GameServerAllocator>.Instance,
            Substitute.For<IWorldOccupancy>());

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
        gameplay.AdvanceAsync(Arg.Any<GameplayWriteAuthority>(), false, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(call => call.ArgAt<DateTime>(2));
        gameplay.ActivateAsync(Arg.Any<GameplayWriteAuthority>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(call => call.ArgAt<DateTime>(1));
        gameplay.RenewAsync(Arg.Any<GameplayWriteAuthority>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(call => call.ArgAt<DateTime>(1));
        Places = new MemoryWorldPlaceStore(_clock, new WorldPlacesConfiguration());
        var outage = new PlaceLedgerOutage(NullLogger<PlaceLedgerOutage>.Instance, _clock);
        Tickets = new(store, crypto, Authorization, sessions, Allocator, options, _clock, applications, Places, outage);
        _fences = new(sessions, Authorization, repositories, accounts, workloads, _clock, applications, Places, outage);
    }

    public async Task<GameAuthReply> Authenticate(uint appId, string expectedState = GameAuthStates.Authorized)
    {
        AuthAttemptReply attempt = (await Authorization.CreateAttemptAsync("steam", GameClientTestData.ClientVersion,
            Guid.NewGuid(), new string('A', 43), null, appId, default))!;
        GameAuthReply reply = await Authorization.AuthenticateSteamAsync(attempt.AttemptCredential,
            (++_proof).ToString("X4", System.Globalization.CultureInfo.InvariantCulture), Guid.NewGuid(), default, "127.0.0.1");
        Assert.Null(reply.Error);
        Assert.Equal(expectedState, reply.State);
        return reply;
    }

    public void Advance(TimeSpan elapsed) => _clock.Advance(elapsed);
    public Task<IActionResult> Worlds(string credential) =>
        Controller().Worlds(new GameContextCredentialRequest { GameContextCredential = credential }, default);

    /// <summary><c>POST /game/join-tickets</c>, or <c>/game/reconnect-tickets</c>, for world 3 unless told another.</summary>
    public Task<IActionResult> Join(string credential, uint? characterId, bool reconnect, ushort worldId = 3)
    {
        var request = new GameJoinRequest { GameContextCredential = credential, WorldId = worldId, CharacterId = characterId };
        GameAdmissionController controller = Controller();
        LastJoin = controller.HttpContext;
        return reconnect ? controller.Reconnect(request, Guid.NewGuid(), default) : controller.Join(request, Guid.NewGuid(), default);
    }

    /// <summary><c>POST /internal/game/join-tickets/redeem</c>, as world 3's workload <c>world-3</c>.</summary>
    public Task<IActionResult> Redeem(string ticket)
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.User = new(new ClaimsIdentity([new Claim(GameServerAuthHandler.ServerIdClaim, "world-3")], GameServerAuthHandler.Scheme));
        var controller = new InternalGameAdmissionController(NullLogger<InternalGameAdmissionController>.Instance, Tickets)
        { ControllerContext = new() { HttpContext = http } };
        return controller.Redeem(new JoinRedemptionRequest { JoinTicket = ticket, ConnectionId = Guid.NewGuid(), RedemptionId = Guid.NewGuid() }, default);
    }

    private GameAdmissionController Controller()
    {
        var controller = new GameAdmissionController(NullLogger<GameAdmissionController>.Instance, Authorization, Tickets, Allocator, new GameApplicationAccessPolicy(Options.Create(Configuration)))
        { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        controller.Request.Scheme = "https";
        return controller;
    }

    public Task<GameSessionLeaseReply> Activate() => _fences.ActivateAsync("world-3", Account.Id, _head!.GameSessionId, _head.FencingToken, default);
    public Task<GameSessionLeaseReply> Heartbeat() => _fences.HeartbeatAsync("world-3", Account.Id, _head!.GameSessionId, _head.FencingToken, default);
}
