using Avalon.Api.Hosting.Worlds;
using Avalon.Api.Identity.Services;
using Avalon.Common.GameAuth;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.GameAuth;
using Avalon.Infrastructure.GameTickets;
using Avalon.Infrastructure.StoreAuth;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.GameAuth;

public sealed class AvalonGameAuthorizationShould
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));
    private readonly AtomicAuthStore _store = new();
    private readonly MemoryGameLicenses _licenses = new();
    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly IRefreshTokenRepository _families = Substitute.For<IRefreshTokenRepository>();
    private readonly IExternalIdentityRepository _identities = Substitute.For<IExternalIdentityRepository>();
    private readonly ILicenseObservationRepository _observations = Substitute.For<ILicenseObservationRepository>();
    private readonly GameAuthCryptography _crypto = new(Enumerable.Range(1, 32).Select(x => (byte)x).ToArray());
    private readonly IOptions<StoreAuthenticationConfiguration> _options = Options.Create(new StoreAuthenticationConfiguration { SteamAppId = 2499460 });
    private readonly Account _account = new() { Id = new(7), Username = "NATIVE", Email = "native@example.test", Salt = [1], Verifier = [2], JoinDate = DateTime.UnixEpoch };
    private readonly Guid _family = Guid.NewGuid();
    private readonly Guid _run = Guid.NewGuid();
    private readonly IGameContextRevocations _revocations = Substitute.For<IGameContextRevocations>();
    private DateTime Now => _clock.GetUtcNow().UtcDateTime;
    public AvalonGameAuthorizationShould()
    {
        _accounts.FindByIdAsync(_account.Id, false, Arg.Any<CancellationToken>()).Returns(_account);
        _families.IsLiveLauncherFamilyAsync(_account.Id, _family, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
    }
    private GameAuthorizationService Service(IGameLicenseRepository? repository = null)
    {
        IGameLicenseRepository licenses = repository ?? _licenses;
        var registry = new GameProviderRegistry([], [new AvalonLicenseProvider(licenses)]);
        return new(_store, new(_store, _crypto, _options, _clock), _crypto, _accounts, _families, _identities,
            registry, new(registry, licenses, _observations, _options, _clock), _options, _clock, revocations: _revocations);
    }
    private GameLicense Grant(DateTime? expires = null, string provider = "avalon")
    {
        var row = new GameLicense
        {
            Id = Guid.NewGuid(),
            AccountId = _account.Id,
            Provider = provider,
            Product = StoreAuthenticationConfiguration.Product,
            Environment = "production",
            ProviderProductId = "base",
            LicenseReference = Guid.NewGuid().ToString("N"),
            AuthorityKind = LicenseAuthorityKind.StoredGrant,
            GrantedAt = Now,
            ExpiresAt = expires
        };
        _licenses.Rows.Add(row); return row;
    }
    private async Task<(GameAuthReply Reply, string Attempt, string Ticket, Guid Request)> Handoff(GameAuthorizationService? service = null)
    {
        service ??= Service();
        AuthAttemptReply attempt = (await service.CreateAttemptAsync("avalon", "1", _run, new string('A', 43), null, null, default))!;
        string ticket = GameAuthCryptography.NewToken(); var request = Guid.NewGuid();
        _store.Seed(RedisGameTicketStore.Key(ticket), $"7|{_family:D}|0|0|production");
        return (await service.RedeemHandoffAsync(attempt.AttemptCredential, ticket, request, default), attempt.AttemptCredential, ticket, request);
    }
    [Fact]
    public async Task Changing_to_Steam_retires_the_original_context_without_rebinding_its_world_session()
    {
        GameLicense grant = Grant();
        GameAuthReply initial = (await Handoff()).Reply;
        GameContextRecord original = (await Service().GetContextAsync(initial.GameContextCredential!, true, default))!;
        var worldSession = new GameSession
        {
            GameContextId = original.Id,
            AccountId = _account.Id,
            ServerId = "world-1",
            Environment = "production",
            GameSessionId = Guid.NewGuid(),
            WorldId = 1,
            FencingToken = 1,
            State = GameSessionState.Active,
            LeaseUntil = Now.AddSeconds(30),
            LicenseUntil = original.AuthorizationValidUntil!.Value
        };
        IGameSessionRepository sessions = Substitute.For<IGameSessionRepository>();
        sessions.FindAsync(_account.Id, Arg.Any<CancellationToken>()).Returns(worldSession);
        sessions.TryRenewAsync(_account.Id, worldSession.GameSessionId, 1, "world-1", 0, 0,
            Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
        IWorldRepositories worlds = Substitute.For<IWorldRepositories>();
        IGameplayFenceRepository fence = Substitute.For<IGameplayFenceRepository>();
        worlds.GameplayFences(new WorldId(1)).Returns(fence);
        fence.RenewAsync(Arg.Any<GameplayWriteAuthority>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
        var heartbeats = new GameSessionFenceService(sessions, Service(), worlds, _accounts,
            Options.Create(new GameWorkloadConfiguration { Servers = [new() { ServerId = "world-1", WorldId = 1 }] }),
            _clock, new(_options));
        Assert.Null((await heartbeats.HeartbeatAsync("world-1", _account.Id, worldSession.GameSessionId, 1, default)).Error);
        const string Subject = "76561198000000001";
        _identities.FindAsync("steam", Subject, Arg.Any<CancellationToken>()).Returns(new ExternalIdentity
        { Id = Guid.NewGuid(), AccountId = _account.Id, Provider = "steam", ProviderSubject = Subject });
        ISteamProofVerifier proof = Substitute.For<ISteamProofVerifier>();
        proof.VerifyAsync(Arg.Any<uint>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SteamProofResult(SteamProofStatus.Verified, Subject));
        ISteamOwnershipClient ownership = Substitute.For<ISteamOwnershipClient>();
        ownership.CheckAsync(2499460, Subject, Arg.Any<CancellationToken>())
            .Returns(new SteamOwnershipResult(SteamOwnershipStatus.Owned, Subject, Now, Now.AddMinutes(5)));
        GameAuthorizationService service = TestGameAuthorization.Create(_store, new(_store, _crypto, _options, _clock), _crypto,
            _accounts, _families, _identities, _observations, proof, ownership, _options, _clock, gameLicenses: _licenses);
        AuthAttemptReply attempt = (await service.CreateProviderAttemptAsync("steam.main", "1", _run, new string('A', 43),
            initial.GameContextCredential, 2499460, default))!;
        GameAuthReply switched = await service.AuthenticateProviderAsync("steam", attempt.AttemptCredential, "ABCD", Guid.NewGuid(), default);
        GameContextRecord replacement = (await service.GetContextAsync(switched.GameContextCredential!, true, default))!;
        Assert.NotEqual(worldSession.GameContextId, replacement.Id);
        Assert.Equal(_family, replacement.LauncherFamilyId);
        Assert.Null(await service.GetContextByIdAsync(worldSession.GameContextId, true, default));
        Assert.Null(await service.GetContextAsync(initial.GameContextCredential!, false, default));
        Assert.Equal(GameAuthErrors.SessionRevoked, (await heartbeats.HeartbeatAsync("world-1", _account.Id, worldSession.GameSessionId, 1, default)).Error);
        grant.RevokedAt = Now; grant.AuthorityRevision++;
        Assert.NotNull(await service.GetContextAsync(switched.GameContextCredential!, true, default));
        Assert.Null(await service.GetContextByIdAsync(worldSession.GameContextId, true, default));
    }

    [Fact]
    public async Task Licensed_handoff_preserves_account_family_and_binds_only_its_Avalon_grant()
    {
        Grant(provider: "other-store"); GameLicense grant = Grant();
        GameAuthReply result = (await Handoff()).Reply;
        Assert.Equal(GameAuthStates.Authorized, result.State); Assert.Equal("avalon", result.LicenseSource);
        GameContextRecord context = (await Service().GetContextAsync(result.GameContextCredential!, true, default))!;
        Assert.Equal(7, context.AccountId); Assert.Equal(_family, context.LauncherFamilyId);
        Assert.Equal("avalon.base", context.ApplicationKey); Assert.Equal(grant.Id, context.LicenseId);
        Assert.Null(context.IdentityVerifiedAt); Assert.Null(context.IdentityValidUntil); Assert.Null(context.ProviderSubject);
        Assert.Equal(Now.AddMinutes(5), context.AuthorizationValidUntil);
        Assert.Empty(_identities.ReceivedCalls());
    }
    [Fact]
    public async Task Pending_handoff_keeps_identity_and_refresh_acquires_first_grant_without_ticket_replay()
    {
        (GameAuthReply Reply, string Attempt, string Ticket, Guid Request) pending = await Handoff(); Assert.Equal(GameAuthStates.PendingLicense, pending.Reply.State);
        Assert.Equal("avalon", pending.Reply.LicenseSource);
        GameContextRecord before = (await Service().GetContextAsync(pending.Reply.GameContextCredential!, false, default))!;
        Assert.Equal(7, before.AccountId); Assert.Equal(_family, before.LauncherFamilyId); Assert.Null(before.LicenseId);
        GameLicense grant = Grant();
        GameAuthReply refreshed = await Service().RefreshAsync(pending.Reply.GameContextRefreshToken!, Guid.NewGuid(), default);
        GameContextRecord after = (await Service().GetContextAsync(refreshed.GameContextCredential!, true, default))!;
        Assert.Equal(before.Id, after.Id); Assert.Equal(grant.Id, after.LicenseId); Assert.Equal("avalon", refreshed.LicenseSource);
        Assert.Null(await _store.ReadAsync(RedisGameTicketStore.Key(pending.Ticket), default));
    }
    [Fact]
    public async Task Native_refresh_renews_bounded_authority_and_honors_earlier_grant_expiry()
    {
        GameLicense grant = Grant(Now.AddMinutes(6)); GameAuthReply initial = (await Handoff()).Reply;
        _clock.Advance(TimeSpan.FromMinutes(4));
        GameAuthReply renewal = await Service().RefreshAsync(initial.GameContextRefreshToken!, Guid.NewGuid(), default);
        Assert.Equal(GameAuthStates.Authorized, renewal.State); Assert.Equal(grant.ExpiresAt, renewal.AuthorizationValidUntil);
        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Null(await Service().GetContextAsync(renewal.GameContextCredential!, true, default));
        GameAuthReply expired = await Service().RefreshAsync(renewal.GameContextRefreshToken!, Guid.NewGuid(), default);
        Assert.Equal(GameAuthStates.PendingLicense, expired.State);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Revocation_including_future_timestamp_invalidates_handoff_and_refresh_receipts(bool future)
    {
        GameLicense grant = Grant(); (GameAuthReply Reply, string Attempt, string Ticket, Guid Request) initial = await Handoff(); var request = Guid.NewGuid();
        GameAuthReply rotated = await Service().RefreshAsync(initial.Reply.GameContextRefreshToken!, request, default);
        Assert.Equal(GameAuthStates.Authorized, rotated.State);
        grant.RevokedAt = future ? Now.AddDays(1) : Now; grant.AuthorityRevision++;
        Grant(); // Replacement grant cannot revive an exact prior license binding.
        Assert.Null(await Service().GetContextAsync(rotated.GameContextCredential!, false, default));
        Assert.Equal(GameAuthErrors.ContextRevoked, (await Service().RefreshAsync(initial.Reply.GameContextRefreshToken!, request, default)).Error);
        Assert.Equal(GameAuthErrors.ContextRevoked, (await Service().RedeemHandoffAsync(initial.Attempt, initial.Ticket, initial.Request, default)).Error);
    }
    [Theory]
    [InlineData("lapsed")]
    [InlineData("license-read")]
    [InlineData("binding-read")]
    public async Task A_refresh_receipt_refused_for_a_lapsed_license_revokes_the_context_but_an_unreadable_license_does_not(string failure)
    {
        bool outage = failure != "lapsed";
        GameLicense grant = Grant(); GameAuthReply initial = (await Handoff()).Reply; var request = Guid.NewGuid();
        GameAuthReply rotated = await Service().RefreshAsync(initial.GameContextRefreshToken!, request, default);
        Assert.Equal(GameAuthStates.Authorized, rotated.State);
        GameContextRecord context = (await Service().GetContextAsync(rotated.GameContextCredential!, true, default))!;
        IGameLicenseRepository licenses = _licenses;
        if (outage)
        {
            // license-read: the binding check reads the row and the license check after it fails to; binding-read:
            // the database is down from the first read.
            licenses = Substitute.For<IGameLicenseRepository>();
            if (failure == "license-read")
            {
                licenses.FindAsync(grant.Id, Arg.Any<CancellationToken>())
                    .Returns(_ => Task.FromResult<GameLicense?>(grant), _ => throw new IOException("database down"));
            }
            else
            {
                licenses.FindAsync(grant.Id, Arg.Any<CancellationToken>()).Returns<Task<GameLicense?>>(_ => throw new IOException("database down"));
            }
        }
        else
        {
            grant.ExpiresAt = Now; // Lapsed, while the binding the context holds is intact.
        }

        GameAuthReply replay = await Service(licenses).RefreshAsync(initial.GameContextRefreshToken!, request, default);

        if (outage)
        {
            Assert.Equal(GameAuthErrors.ProviderUnavailable, replay.Error);
            Assert.NotNull(await Service().GetContextAsync(rotated.GameContextCredential!, true, default));
            // The outage outlasts the receipt's own lifetime; the same retry still finds its receipt once it is over.
            _clock.Advance(GameAuthPolicy.RefreshReceiptLifetime + TimeSpan.FromSeconds(1));
            Assert.Equal(rotated, await Service().RefreshAsync(initial.GameContextRefreshToken!, request, default));
            await _revocations.DidNotReceiveWithAnyArgs().PublishAsync(default!, default);
        }
        else
        {
            Assert.Equal(GameAuthErrors.ContextRevoked, replay.Error);
            Assert.Null(await Service().GetContextByIdAsync(context.Id, false, default));
            Assert.Equal(GameAuthErrors.ContextRevoked, (await Service().RefreshAsync(rotated.GameContextRefreshToken!, Guid.NewGuid(), default)).Error);
            await _revocations.Received(1).PublishAsync(_account.Id, context.Id);
        }
    }
    [Theory]
    [InlineData("account")]
    [InlineData("family")]
    [InlineData("epoch")]
    public async Task Account_and_launcher_family_changes_refuse_renewal(string changed)
    {
        Grant(); GameAuthReply initial = (await Handoff()).Reply;
        if (changed == "account") _account.Status = AccountStatus.Banned;
        if (changed == "epoch") _account.SessionEpoch++;
        if (changed == "family") _families.IsLiveLauncherFamilyAsync(_account.Id, _family, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(false);
        Assert.Equal(GameAuthErrors.ContextRevoked, (await Service().RefreshAsync(initial.GameContextRefreshToken!, Guid.NewGuid(), default)).Error);
    }
    [Fact]
    public async Task Database_failure_cannot_extend_existing_authority()
    {
        Grant(); GameAuthReply initial = (await Handoff()).Reply;
        IGameLicenseRepository unavailable = Substitute.For<IGameLicenseRepository>();
        unavailable.FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns<Task<GameLicense?>>(_ => throw new IOException("database down"));
        GameAuthReply refused = await Service(unavailable).RefreshAsync(initial.GameContextRefreshToken!, Guid.NewGuid(), default);
        Assert.Equal(GameAuthErrors.ProviderUnavailable, refused.Error);
        Assert.Null(refused.GameContextCredential);
        // An outage neither rotated nor revoked the context: once the database is back, the same token renews it.
        Assert.Equal(GameAuthStates.Authorized, (await Service().RefreshAsync(initial.GameContextRefreshToken!, Guid.NewGuid(), default)).State);
    }
    [Fact]
    public async Task Unknown_native_provider_is_not_an_attempt_authority()
    {
        var registry = new GameProviderRegistry([], []);
        var service = new GameAuthorizationService(_store, new(_store, _crypto, _options, _clock), _crypto, _accounts, _families, _identities,
            registry, new(registry, _licenses, _observations, _options, _clock), _options, _clock);
        Assert.Null(await service.CreateProviderAttemptAsync("avalon.base", "1", _run, new string('A', 43), null, 0, default));
    }

    [Fact]
    public async Task Suspension_blocks_native_refresh_and_cached_handoff_even_before_revision_changes()
    {
        GameLicense grant = Grant();
        (GameAuthReply Reply, string Attempt, string Ticket, Guid Request) initial = await Handoff();
        grant.SuspendedAt = Now;
        Assert.Null(await Service().GetContextAsync(initial.Reply.GameContextCredential!, false, default));
        Assert.Equal(GameAuthErrors.ContextRevoked, (await Service().RefreshAsync(initial.Reply.GameContextRefreshToken!, Guid.NewGuid(), default)).Error);
        Assert.Equal(GameAuthErrors.ContextRevoked, (await Service().RedeemHandoffAsync(initial.Attempt, initial.Ticket, initial.Request, default)).Error);
    }

    [Fact]
    public async Task Restoration_requires_fresh_context_and_never_revives_old_receipts()
    {
        GameLicense grant = Grant();
        (GameAuthReply Reply, string Attempt, string Ticket, Guid Request) initial = await Handoff();
        grant.SuspendedAt = Now; grant.AuthorityRevision++;
        Assert.Null(await Service().GetContextAsync(initial.Reply.GameContextCredential!, true, default));
        grant.SuspendedAt = null; grant.AuthorityRevision++;
        Assert.Equal(GameAuthErrors.ContextRevoked, (await Service().RefreshAsync(initial.Reply.GameContextRefreshToken!, Guid.NewGuid(), default)).Error);
        Assert.Equal(GameAuthErrors.ContextRevoked, (await Service().RedeemHandoffAsync(initial.Attempt, initial.Ticket, initial.Request, default)).Error);
        Assert.Equal(GameAuthStates.Authorized, (await Handoff()).Reply.State);
    }
}
