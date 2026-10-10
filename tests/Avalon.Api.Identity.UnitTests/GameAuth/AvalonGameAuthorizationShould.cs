using Avalon.Api.Contract;
using Avalon.Api.Hosting.Worlds;
using Avalon.Api.Identity.Controllers;
using Avalon.Api.Identity.Services;
using Avalon.Common.GameAuth;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.GameAuth;
using Avalon.Infrastructure.GameTickets;
using Avalon.Infrastructure.StoreAuth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;
using AccountStatus = Avalon.Domain.Auth.AccountStatus;

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
    private async Task<(GameAuthReply Reply, string Attempt, string Ticket, Guid Request)> Handoff(GameAuthorizationService? service = null,
        string protocol = "1")
    {
        service ??= Service();
        AuthAttemptReply attempt = (await service.CreateAttemptAsync("avalon", protocol, _run, new string('A', 43), null, null, default))!;
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
            initial.GameContextCredential, 2499460, default)).Attempt!;
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
    [InlineData("lapsed", "")]
    [InlineData("license-fault", "")]
    [InlineData("license-read", "recovered")]
    [InlineData("database-down", "recovered")]
    [InlineData("database-down", "too-late")]
    [InlineData("database-down", "near-expiry")]
    [InlineData("database-down", "reuse")]
    [InlineData("database-down", "superseded")]
    public async Task A_refresh_receipt_refused_for_a_lapsed_license_revokes_the_context_but_an_outage_keeps_it_for_its_retry(string failure, string after)
    {
        GameLicense grant = Grant(); GameAuthReply initial = (await Handoff()).Reply; var request = Guid.NewGuid();
        GameAuthReply rotated = await Service().RefreshAsync(initial.GameContextRefreshToken!, request, default);
        Assert.Equal(GameAuthStates.Authorized, rotated.State);
        GameContextRecord context = (await Service().GetContextAsync(rotated.GameContextCredential!, true, default))!;
        string contextKey = Avalon.Infrastructure.CacheKeys.GameAuth("production", "context", context.Id.ToString("N"));
        if (after == "near-expiry")
        {
            context = context with { AbsoluteExpiresAt = Now.AddMinutes(2) };
            _store.Seed(contextKey, GameAuthJson.Serialize(context));
        }

        string tokenKey = Avalon.Infrastructure.CacheKeys.GameAuth("production", "token", GameAuthCryptography.Digest(initial.GameContextRefreshToken!));
        DateTime? issued = GameAuthJson.Deserialize<GameAuthTokenRecord>(await _store.ReadAsync(tokenKey, default))!.ReceiptExpiresAt;
        if (after == "superseded")
        {
            // A later refresh replaced the first rotation: its receipt is no longer the newest and is not kept.
            GameAuthReply again = await Service().RefreshAsync(rotated.GameContextRefreshToken!, Guid.NewGuid(), default);
            context = (await Service().GetContextAsync(again.GameContextCredential!, true, default))!;
        }

        if (failure == "lapsed")
        {
            grant.ExpiresAt = Now; // Lapsed, while the binding the context holds is intact.
            Assert.Equal(GameAuthErrors.ContextRevoked, (await Service().RefreshAsync(initial.GameContextRefreshToken!, request, default)).Error);
            Assert.Null(await Service().GetContextByIdAsync(context.Id, false, default));
            Assert.Equal(GameAuthErrors.ContextRevoked, (await Service().RefreshAsync(rotated.GameContextRefreshToken!, Guid.NewGuid(), default)).Error);
            await _revocations.Received(1).PublishAsync(_account.Id, context.Id);
            return;
        }

        if (failure == "license-fault")
        {
            // A fault in the license read (not an outage) reaches the error handler: nothing is revoked or refused.
            IGameLicenseRepository faulty = Substitute.For<IGameLicenseRepository>();
            faulty.FindAsync(grant.Id, Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult<GameLicense?>(grant), _ => throw new InvalidOperationException("a second operation"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service(faulty).RefreshAsync(initial.GameContextRefreshToken!, request, default));
            Assert.NotNull(await Service().GetContextAsync(rotated.GameContextCredential!, true, default));
            // The exact retry's receipt is kept as for an outage, so the same retry after the fault still finds it.
            Assert.Equal(Now.Add(GameAuthPolicy.OutageReceiptLifetime),
                GameAuthJson.Deserialize<GameAuthTokenRecord>(await _store.ReadAsync(tokenKey, default))!.ReceiptExpiresAt);
            _clock.Advance(GameAuthPolicy.RefreshReceiptLifetime + TimeSpan.FromSeconds(1));
            Assert.Equal(rotated, await Service().RefreshAsync(initial.GameContextRefreshToken!, request, default));
            await _revocations.DidNotReceiveWithAnyArgs().PublishAsync(default!, default);
            return;
        }

        // license-read: every read works but the receipt's license check; database-down: the account read, the first.
        IGameLicenseRepository licenses = Substitute.For<IGameLicenseRepository>();
        licenses.FindAsync(grant.Id, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<GameLicense?>(grant), _ => throw new IOException("database down"));
        if (failure == "database-down")
            _accounts.FindByIdAsync(_account.Id, false, Arg.Any<CancellationToken>()).Returns<Account?>(_ => throw new IOException("database down"));
        Assert.Equal(GameAuthErrors.ProviderUnavailable, (await Service(licenses).RefreshAsync(initial.GameContextRefreshToken!, request, default)).Error);
        _accounts.FindByIdAsync(_account.Id, false, Arg.Any<CancellationToken>()).Returns(_account);
        Assert.Equal(context, await _store.ReadAsync(contextKey, default) is { } raw ? GameAuthJson.Deserialize<GameContextRecord>(raw) : null);
        DateTime? kept = GameAuthJson.Deserialize<GameAuthTokenRecord>(await _store.ReadAsync(tokenKey, default))!.ReceiptExpiresAt;

        switch (after)
        {
            case "recovered":
                // The outage outlasts the receipt's own 30 s; the same retry still finds its receipt once it is over.
                Assert.Equal(Now.Add(GameAuthPolicy.OutageReceiptLifetime), kept);
                _clock.Advance(GameAuthPolicy.RefreshReceiptLifetime + TimeSpan.FromSeconds(1));
                Assert.Equal(rotated, await Service().RefreshAsync(initial.GameContextRefreshToken!, request, default));
                await _revocations.DidNotReceiveWithAnyArgs().PublishAsync(default!, default);
                break;
            case "too-late":
                // Past the window the outage answer gave, the retry is a reuse of the spent token.
                _clock.Advance(GameAuthPolicy.OutageReceiptLifetime + TimeSpan.FromSeconds(1));
                Assert.Equal(GameAuthErrors.RefreshReuse, (await Service().RefreshAsync(initial.GameContextRefreshToken!, request, default)).Error);
                Assert.Null(await Service().GetContextByIdAsync(context.Id, false, default));
                await _revocations.Received(1).PublishAsync(_account.Id, context.Id);
                break;
            case "reuse":
                // A spent token sent under another key is a reuse, caught with the database still down.
                _accounts.FindByIdAsync(_account.Id, false, Arg.Any<CancellationToken>()).Returns<Account?>(_ => throw new IOException("database down"));
                Assert.Equal(GameAuthErrors.RefreshReuse, (await Service(licenses).RefreshAsync(initial.GameContextRefreshToken!, Guid.NewGuid(), default)).Error);
                Assert.Equal(GameAuthStates.Revoked, GameAuthJson.Deserialize<GameContextRecord>(await _store.ReadAsync(contextKey, default))!.State);
                await _revocations.Received(1).PublishAsync(_account.Id, context.Id);
                break;
            case "superseded":
                Assert.Equal(issued, kept);
                break;
            case "near-expiry":
                Assert.Equal(context.AbsoluteExpiresAt, kept); // Never past the context's absolute expiry.
                break;
        }
    }
    [Theory]
    [InlineData("family")]
    [InlineData("epoch")]
    public async Task Account_and_launcher_family_changes_refuse_renewal(string changed)
    {
        Grant(); GameAuthReply initial = (await Handoff()).Reply;
        if (changed == "epoch") _account.SessionEpoch++;
        if (changed == "family") _families.IsLiveLauncherFamilyAsync(_account.Id, _family, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(false);
        Assert.Equal(GameAuthErrors.ContextRevoked, (await Service().RefreshAsync(initial.GameContextRefreshToken!, Guid.NewGuid(), default)).Error);
    }
    [Theory]
    [InlineData("binding-read")]
    [InlineData("license-reads")]
    [InlineData("account-read")]
    [InlineData("fault")]
    [InlineData("lost-race")]
    public async Task Database_failure_cannot_extend_existing_authority(string failing)
    {
        GameLicense grant = Grant(); GameAuthReply initial = (await Handoff()).Reply;
        if (failing == "lost-race")
        {
            // Another decision applied first: not a refusal, so the renewal keeps the authority that still stands.
            _licenses.LoseDecisionRace = true;
            GameAuthReply kept = await Service().RefreshAsync(initial.GameContextRefreshToken!, Guid.NewGuid(), default);
            Assert.Equal(GameAuthStates.Authorized, kept.State);
            Assert.Equal(GameAuthErrors.ProviderUnavailable, kept.Error);
            return;
        }

        IGameLicenseRepository unavailable = Substitute.For<IGameLicenseRepository>();
        if (failing == "license-reads") // The binding read works; the renewal and the current-license check do not.
            unavailable.FindAsync(grant.Id, Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<GameLicense?>(grant), _ => throw new IOException("database down"));
        else
            unavailable.FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns<Task<GameLicense?>>(_ => throw new IOException("database down"));
        if (failing == "account-read")
            _accounts.FindByIdAsync(_account.Id, false, Arg.Any<CancellationToken>()).Returns<Account?>(_ => throw new IOException("database down"));
        if (failing == "fault")
        {
            // A fault in the code is not an outage: it reaches the error handler, never a 503 nor a refusal.
            _accounts.FindByIdAsync(_account.Id, false, Arg.Any<CancellationToken>()).Returns<Account?>(_ => throw new InvalidOperationException("a second operation"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service(_licenses).RefreshAsync(initial.GameContextRefreshToken!, Guid.NewGuid(), default));
            return;
        }
        GameAuthReply refused = await Service(unavailable).RefreshAsync(initial.GameContextRefreshToken!, Guid.NewGuid(), default);
        _accounts.FindByIdAsync(_account.Id, false, Arg.Any<CancellationToken>()).Returns(_account);
        Assert.Equal(GameAuthErrors.ProviderUnavailable, refused.Error);
        Assert.Null(refused.GameContextCredential);
        // An outage neither rotated nor revoked the context: once the database is back, the same token renews it.
        Assert.Equal(GameAuthStates.Authorized, (await Service().RefreshAsync(initial.GameContextRefreshToken!, Guid.NewGuid(), default)).State);
    }
    /// <summary>
    /// #882: every game route answered an inactive account as it answers a bad credential, so a banned player was told
    /// only that authorization was refused. The status is now named (403), but only to a caller whose proof is current
    /// on its own terms and whose account is still at that proof's credentials version; the ban's own epoch move and
    /// launcher revocation do not hide it. A proof made stale (a credentials change, a logout, a reuse) or never made
    /// keeps the generic answer. The password lock no longer gates an identity proven another way.
    /// </summary>
    [Theory]
    [InlineData("handoff", "banned", 403, "ACCOUNT_BANNED")]
    [InlineData("handoff", "deactivated", 403, "ACCOUNT_DEACTIVATED")]
    [InlineData("handoff", "consolidating", 403, "ACCOUNT_CONSOLIDATING")]
    [InlineData("steam", "banned", 403, "ACCOUNT_BANNED")]
    [InlineData("steam-carried", "banned", 403, "ACCOUNT_BANNED")]
    [InlineData("join", "banned", 403, "ACCOUNT_BANNED")]
    [InlineData("refresh", "banned", 403, "ACCOUNT_BANNED")]
    [InlineData("refresh", "consolidating", 403, "ACCOUNT_CONSOLIDATING")]
    [InlineData("attempt", "deactivated", 403, "ACCOUNT_DEACTIVATED")]
    [InlineData("worlds", "banned", 403, "ACCOUNT_BANNED")]
    [InlineData("handoff", "banned-after-password-change", 401, "INVALID_HANDOFF")]
    [InlineData("handoff", "banned-unknown-ticket", 401, "INVALID_HANDOFF")]
    [InlineData("steam", "banned-unverified-proof", 401, "INVALID_PROOF")]
    [InlineData("steam-carried", "banned-unverified-proof", 401, "INVALID_PROOF")]
    [InlineData("attempt", "banned-after-password-change", 400, "INVALID_ATTEMPT")]
    [InlineData("attempt", "banned-wrong-run", 400, "INVALID_ATTEMPT")]
    [InlineData("worlds", "banned-after-password-change", 401, "ACCOUNT_REQUIRED")]
    [InlineData("refresh", "banned-after-password-change", 401, "CONTEXT_REVOKED")]
    [InlineData("refresh", "banned-after-logout", 401, "CONTEXT_REVOKED")]
    [InlineData("refresh", "banned-after-reuse", 401, "REFRESH_REUSE")]
    [InlineData("worlds", "banned-after-logout", 401, "ACCOUNT_REQUIRED")]
    [InlineData("handoff", "locked", 200, null)]
    [InlineData("steam", "locked", 200, null)]
    [InlineData("refresh", "locked", 200, null)]
    public async Task Name_an_inactive_account_only_to_a_caller_whose_proof_is_current(string route, string change, int status, string? error)
    {
        Grant();
        const string Subject = "76561198000000001";
        _identities.FindAsync("steam", Subject, Arg.Any<CancellationToken>()).Returns(new ExternalIdentity
        { Id = Guid.NewGuid(), AccountId = _account.Id, Provider = "steam", ProviderSubject = Subject });
        ISteamProofVerifier proof = Substitute.For<ISteamProofVerifier>();
        proof.VerifyAsync(Arg.Any<uint>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
            change == "banned-unverified-proof" ? new SteamProofResult(SteamProofStatus.InvalidProof) : new SteamProofResult(SteamProofStatus.Verified, Subject));
        ISteamOwnershipClient ownership = Substitute.For<ISteamOwnershipClient>();
        ownership.CheckAsync(2499460, Subject, Arg.Any<CancellationToken>())
            .Returns(_ => new SteamOwnershipResult(SteamOwnershipStatus.Owned, Subject, Now, Now.AddMinutes(5)));
        GameAuthorizationService service = TestGameAuthorization.Create(_store, new(_store, _crypto, _options, _clock), _crypto,
            _accounts, _families, _identities, _observations, proof, ownership, _options, _clock, revocations: _revocations, gameLicenses: _licenses);
        var auth = new GameAuthController(service) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        auth.Request.Scheme = "https";
        var policy = new GameApplicationAccessPolicy(_options);
        IGameServerAllocator allocator = Substitute.For<IGameServerAllocator>();
        allocator.ListAsync(Arg.Any<GameContextRecord>(), Arg.Any<CancellationToken>()).Returns([]);
        var tickets = new JoinTicketStore(_store, _crypto, service, Substitute.For<IGameSessionRepository>(), allocator, _options, _clock, policy);
        var admission = new GameAdmissionController(service, tickets, allocator, policy)
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() },
        };
        admission.Request.Scheme = "https";

        // A context signed in before the change, for the routes that present one.
        const string Protocol = GameWorkloadConfiguration.ClientProtocolVersion;
        GameAuthReply? context = route is "refresh" or "attempt" or "worlds" or "join" or "steam-carried"
            ? (await Handoff(service, Protocol)).Reply : null;
        if (change == "banned-after-logout") await service.LogoutAsync(context!.GameContextCredential!, default);
        if (change == "banned-after-reuse") await service.RefreshAsync(context!.GameContextRefreshToken!, Guid.NewGuid(), default);
        AuthAttemptReply? attempt = route switch
        {
            "handoff" => await service.CreateAttemptAsync("avalon", Protocol, _run, new string('A', 43), null, null, default),
            "steam" => await service.CreateAttemptAsync("steam", Protocol, _run, new string('A', 43), null, null, default),
            // An attempt carrying the signed-in context, made while the account could still play.
            "steam-carried" => await service.CreateAttemptAsync("steam", Protocol, _run, new string('A', 43), context!.GameContextCredential, null, default),
            _ => null,
        };
        string ticket = GameAuthCryptography.NewToken();
        if (change != "banned-unknown-ticket") _store.Seed(RedisGameTicketStore.Key(ticket), $"7|{_family:D}|0|0|production");

        // What the change does to the account, with the side effects the real one has: a ban, a deactivation and a
        // consolidation move the session epoch and end the launcher's session.
        if (change.StartsWith("banned", StringComparison.Ordinal)) _account.Status = AccountStatus.Banned;
        if (change == "deactivated") _account.Status = AccountStatus.Deactivated;
        if (change == "consolidating") _account.GameplayConsolidationId = Guid.NewGuid();
        if (change != "locked")
        {
            _account.SessionEpoch++;
            _families.IsLiveLauncherFamilyAsync(_account.Id, _family, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(false);
        }
        if (change.EndsWith("after-password-change", StringComparison.Ordinal)) _account.CredentialsVersion++;
        if (change == "locked")
        {
            _account.Locked = true;
            _account.LockedUntil = Now.AddMinutes(15);
        }

        IActionResult result = route switch
        {
            "handoff" => await auth.Handoff(new GameHandoffRequest { AttemptCredential = attempt!.AttemptCredential, HandoffTicket = ticket }, Guid.NewGuid(), default),
            "steam" or "steam-carried" => await auth.ProviderProof(new GameProviderProofRequest { Provider = "steam", AttemptCredential = attempt!.AttemptCredential, Proof = "ABCD" }, Guid.NewGuid(), default),
            "join" => await admission.Join(new GameJoinRequest { GameContextCredential = context!.GameContextCredential!, WorldId = 1 }, Guid.NewGuid(), default),
            "refresh" => await auth.Refresh(new GameContextRefreshRequest { GameContextRefreshToken = context!.GameContextRefreshToken! }, Guid.NewGuid(), default),
            "attempt" => await auth.ProviderAttempt(new GameProviderAttemptRequest
            {
                ApplicationKey = "steam.main",
                ProtocolVersion = Protocol,
                ClientRunId = change == "banned-wrong-run" ? Guid.NewGuid() : _run,
                LinkChallenge = new string('A', 43),
                GameContextCredential = context!.GameContextCredential,
            }, default),
            _ => await admission.Worlds(new GameContextCredentialRequest { GameContextCredential = context!.GameContextCredential! }, default),
        };

        ObjectResult answer = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(status, answer.StatusCode);
        Assert.Equal(error, answer.Value switch { GameAuthReply reply => reply.Error, GameJoinReply join => join.Error, _ => null });
    }

    [Fact]
    public async Task Unknown_native_provider_is_not_an_attempt_authority()
    {
        var registry = new GameProviderRegistry([], []);
        var service = new GameAuthorizationService(_store, new(_store, _crypto, _options, _clock), _crypto, _accounts, _families, _identities,
            registry, new(registry, _licenses, _observations, _options, _clock), _options, _clock);
        Assert.Null((await service.CreateProviderAttemptAsync("avalon.base", "1", _run, new string('A', 43), null, 0, default)).Attempt);
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
