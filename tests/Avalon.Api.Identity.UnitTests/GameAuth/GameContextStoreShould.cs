using Avalon.Api.Testing;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.GameAuth;
using Avalon.Infrastructure.GameTickets;
using Avalon.Infrastructure.StoreAuth;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.GameAuth;

public class GameContextStoreShould
{
    private readonly IGameContextRevocations _revocations = Substitute.For<IGameContextRevocations>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero));
    private readonly AtomicAuthStore _store = new();
    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly IRefreshTokenRepository _families = Substitute.For<IRefreshTokenRepository>();
    private readonly IExternalIdentityRepository _links = Substitute.For<IExternalIdentityRepository>();
    private readonly ILicenseObservationRepository _licenses = Substitute.For<ILicenseObservationRepository>();
    private readonly ISteamProofVerifier _proof = Substitute.For<ISteamProofVerifier>();
    private readonly ISteamOwnershipClient _ownership = Substitute.For<ISteamOwnershipClient>();
    private readonly GameAuthCryptography _crypto = new(new byte[32].Select((_, i) => (byte)(i + 1)).ToArray());
    private readonly IOptions<StoreAuthenticationConfiguration> _config = Options.Create(new StoreAuthenticationConfiguration { SteamAppId = StoreAuthenticationTestData.SteamAppId, SteamPublisherKey = "test-secret" });
    private readonly Account _account = new() { Id = new AccountId(7), Username = "PLAYER", Email = "player@example.test", Salt = [1], Verifier = [2], JoinDate = DateTime.UnixEpoch };
    private readonly Guid _family = Guid.NewGuid();

    public GameContextStoreShould()
    {
        _accounts.FindByIdAsync(Arg.Any<AccountId>(), false, Arg.Any<CancellationToken>()).Returns(_account);
        _families.IsLiveLauncherFamilyAsync(Arg.Any<AccountId>(), _family, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
        _proof.VerifyAsync(Arg.Any<uint>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new SteamProofResult(SteamProofStatus.Verified, "76561198000000001"));
        _links.FindAsync("steam", "76561198000000001", Arg.Any<CancellationToken>()).Returns(new ExternalIdentity
        {
            Id = Guid.NewGuid(),
            AccountId = _account.Id,
            Provider = "steam",
            ProviderSubject = "76561198000000001",
        });
        _ownership.CheckAsync(Arg.Any<uint>(), "76561198000000001", Arg.Any<CancellationToken>()).Returns(call => new SteamOwnershipResult(
            SteamOwnershipStatus.Owned, "76561198000000001", _clock.GetUtcNow().UtcDateTime, _clock.GetUtcNow().UtcDateTime.AddMinutes(5)));
    }

    private GameAuthorizationService Service(IGameContextStore? customStore = null) => TestGameAuthorization.Create(customStore ?? _store,
        new AuthAttemptStore(customStore ?? _store, _crypto, _config, _clock), _crypto,
        _accounts, _families, _links, _licenses, _proof, _ownership, _config, _clock, revocations: _revocations,
        gameLicenses: TestGameAuthorization.Licenses(_store));

    private Task<AuthAttemptReply?> Attempt(string channel, string? context = null, uint? appId = null) =>
        Service().CreateAttemptAsync(channel, "1", Guid.Parse("11111111-1111-1111-1111-111111111111"), new string('A', 43), context, appId, CancellationToken.None);

    [Fact]
    public async Task Steam_authorization_binds_the_shared_license_revision_and_configured_application()
    {
        AuthAttemptReply attempt = (await Attempt("steam"))!;
        GameAuthReply reply = await Service().AuthenticateSteamAsync(attempt.AttemptCredential, "ABCD", Guid.NewGuid(), default);
        GameContextRecord? context = await Service().GetContextAsync(reply.GameContextCredential!, true, default);
        Assert.NotNull(context);
        Assert.Equal("steam.main", context.ApplicationKey);
        Assert.NotNull(context.LicenseId);
        Assert.Equal(1, context.LicenseRevision);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime.AddMinutes(30), context.IdentityValidUntil);
    }

    [Fact]
    public async Task Fresh_ownership_authorizes_a_new_revision_without_reviving_old_contexts_or_receipts()
    {
        AuthAttemptReply first = (await Attempt("steam"))!;
        var request = Guid.NewGuid();
        GameAuthReply initial = await Service().AuthenticateSteamAsync(first.AttemptCredential, "ABCD", request, default);
        GameContextRecord context = (await Service().GetContextAsync(initial.GameContextCredential!, true, default))!;
        _clock.Advance(TimeSpan.FromSeconds(1));
        _ownership.CheckAsync(Arg.Any<uint>(), "76561198000000001", Arg.Any<CancellationToken>()).Returns(_ =>
            new SteamOwnershipResult(SteamOwnershipStatus.NotOwned, "76561198000000001", _clock.GetUtcNow().UtcDateTime, _clock.GetUtcNow().UtcDateTime));
        AuthAttemptReply renewal = (await Attempt("steam", initial.GameContextCredential))!;
        await Service().AuthenticateSteamAsync(renewal.AttemptCredential, "DCBA", Guid.NewGuid(), default);
        Assert.Null(await Service().GetContextByIdAsync(context.Id, true, default));
        Assert.Equal("CONTEXT_REVOKED", (await Service().AuthenticateSteamAsync(first.AttemptCredential, "ABCD", request, default)).Error);
        _clock.Advance(TimeSpan.FromSeconds(1));
        _ownership.CheckAsync(Arg.Any<uint>(), "76561198000000001", Arg.Any<CancellationToken>()).Returns(_ =>
            new SteamOwnershipResult(SteamOwnershipStatus.Owned, "76561198000000001", _clock.GetUtcNow().UtcDateTime, _clock.GetUtcNow().UtcDateTime.AddMinutes(5)));
        AuthAttemptReply fresh = (await Attempt("steam"))!;
        GameAuthReply result = await Service().AuthenticateSteamAsync(fresh.AttemptCredential, "AABB", Guid.NewGuid(), default);
        GameContextRecord next = (await Service().GetContextAsync(result.GameContextCredential!, true, default))!;
        Assert.Equal(context.LicenseId, next.LicenseId);
        Assert.Equal(3, next.LicenseRevision);
        Assert.Null(await Service().GetContextByIdAsync(context.Id, true, default));
    }

    [Theory]
    [InlineData(2499460u, false, "production")]
    [InlineData(2514590u, true, "production")]
    [InlineData(uint.MaxValue, false, "production")]
    [InlineData(2499460u, false, "development")]
    public async Task Create_native_compatible_unique_128_bit_identities_that_reach_the_selected_provider(uint appId, bool playtest, string environment)
    {
        _config.Value.SteamAppId = playtest ? 2499460 : appId;
        _config.Value.SteamPlaytest = new() { Enabled = playtest, AppId = playtest ? appId : 0, AllowedWorldIds = playtest ? [3] : [] };
        _config.Value.Environment = environment;
        _config.Value.SteamIdentityPrefix = environment == "production" ? "avalon-auth-prod" : "avalon-auth-dev";
        AuthAttemptReply attempt = (await Attempt("steam", appId: appId))!;
        AuthAttemptReply another = (await Attempt("steam", appId: appId))!;
        Assert.InRange(attempt.ExpectedSteamIdentity.Length, 1, 31);
        Assert.Matches("^[a-z2-7]{28}$", attempt.ExpectedSteamIdentity);
        Assert.DoesNotContain(':', attempt.ExpectedSteamIdentity);
        Assert.NotEqual(attempt.ExpectedSteamIdentity, another.ExpectedSteamIdentity);
        string prefix = (environment == "production" ? "p" : "d") + (playtest ? "t" : "m");
        Assert.StartsWith(prefix, attempt.ExpectedSteamIdentity);
        Assert.Equal(16, OtpNet.Base32Encoding.ToBytes(attempt.ExpectedSteamIdentity[prefix.Length..]).Length);
        using var handler = new Avalon.Api.Identity.UnitTests.StoreAuth.RecordingSteamHandler
        {
            Body = "{\"response\":{\"params\":{\"result\":\"OK\",\"steamid\":\"76561198000000001\"}}}",
        };
        using var client = new HttpClient(handler);
        SteamProofResult result = await new SteamProofVerifier(client, _config).VerifyAsync(appId, "ABCD", attempt.ExpectedSteamIdentity, default);
        Assert.Equal(SteamProofStatus.Verified, result.Status);
        Uri request = Assert.Single(handler.Requests);
        Assert.Contains("appid=" + appId.ToString(System.Globalization.CultureInfo.InvariantCulture), request.Query, StringComparison.Ordinal);
        Assert.Contains("identity=" + Uri.EscapeDataString(attempt.ExpectedSteamIdentity), request.Query, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2499460u)]
    [InlineData(2514590u)]
    public async Task Selected_application_is_bound_to_challenge_context_and_both_provider_calls(uint appId)
    {
        _config.Value.SteamAppId = 2499460;
        _config.Value.SteamPlaytest = new() { Enabled = true, AppId = 2514590, AllowedWorldIds = [3] };
        _proof.VerifyAsync(appId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SteamProofResult(SteamProofStatus.Verified, "76561198000000001"));
        _ownership.CheckAsync(appId, "76561198000000001", Arg.Any<CancellationToken>())
            .Returns(new SteamOwnershipResult(SteamOwnershipStatus.Owned, "76561198000000001", _clock.GetUtcNow().UtcDateTime, _clock.GetUtcNow().UtcDateTime.AddMinutes(5)));
        AuthAttemptReply attempt = (await Attempt("steam", appId: appId))!;
        Assert.StartsWith(appId == 2514590 ? "pt" : "pm", attempt.ExpectedSteamIdentity);
        AuthAttemptRecord stored = GameAuthJson.Deserialize<AuthAttemptRecord>(await _store.ReadAsync(new AuthAttemptStore(_store, _crypto, _config, _clock).Key(attempt.AttemptCredential), default))!;
        Assert.Equal(appId, stored.SteamAppId);
        GameAuthReply reply = await Service().AuthenticateSteamAsync(attempt.AttemptCredential, "ABCD", Guid.NewGuid(), default);
        GameContextRecord context = (await Service().GetContextAsync(reply.GameContextCredential!, true, default))!;
        Assert.Equal(appId, context.SteamAppId);
        await _proof.Received(1).VerifyAsync(appId, "ABCD", attempt.ExpectedSteamIdentity, Arg.Any<CancellationToken>());
        await _ownership.Received(1).CheckAsync(appId, "76561198000000001", Arg.Any<CancellationToken>());
        Assert.Null(await Attempt("steam", reply.GameContextCredential, appId == 2499460 ? 2514590u : 2499460u));
        GameAuthReply refreshed = await Service().RefreshAsync(reply.GameContextRefreshToken!, Guid.NewGuid(), default);
        Assert.Equal(appId, (await Service().GetContextAsync(refreshed.GameContextCredential!, true, default))!.SteamAppId);
        string key = CacheKeys.GameAuth("production", "context", context.Id.ToString("N"));
        GameContextRecord current = (await Service().GetContextAsync(refreshed.GameContextCredential!, true, default))!;
        _store.Seed(key, GameAuthJson.Serialize(current with { ApplicationKey = "disabled" }));
        Assert.Null(await Service().GetContextAsync(refreshed.GameContextCredential!, true, default));
    }

    [Theory]
    [InlineData("steam", 0u)]
    [InlineData("steam", 480u)]
    [InlineData("avalon", 2514590u)]
    public async Task Invalid_selector_or_playtest_handoff_creates_no_attempt(string channel, uint appId)
    {
        _config.Value.SteamAppId = 2499460;
        _config.Value.SteamPlaytest = new() { Enabled = true, AppId = 2514590, AllowedWorldIds = [3] };
        Assert.Null(await Attempt(channel, appId: appId));
        Assert.Empty(_store.Entries);
    }

    [Theory]
    [InlineData(2499460u, 2514590u)]
    [InlineData(2514590u, 2499460u)]
    public async Task Negative_observation_revokes_only_the_context_for_its_application(uint revokedApp, uint unaffectedApp)
    {
        _config.Value.SteamAppId = 2499460;
        _config.Value.SteamPlaytest = new() { Enabled = true, AppId = 2514590, AllowedWorldIds = [3] };
        AuthAttemptReply revokedAttempt = (await Attempt("steam", appId: revokedApp))!;
        AuthAttemptReply otherAttempt = (await Attempt("steam", appId: unaffectedApp))!;
        GameAuthReply revoked = await Service().AuthenticateSteamAsync(revokedAttempt.AttemptCredential, "ABCD", Guid.NewGuid(), default);
        GameAuthReply unaffected = await Service().AuthenticateSteamAsync(otherAttempt.AttemptCredential, "DCBA", Guid.NewGuid(), default);
        Assert.NotNull(await Service().GetContextAsync(revoked.GameContextCredential!, true, default));
        GameContextRecord bound = (await Service().GetContextAsync(revoked.GameContextCredential!, true, default))!;
        await TestGameAuthorization.Licenses(_store).ApplyDecisionAsync(bound.LicenseId!.Value, bound.LicenseRevision!.Value,
            new(false, _clock.GetUtcNow().UtcDateTime, _clock.GetUtcNow().UtcDateTime));
        Assert.Null(await Service().GetContextAsync(revoked.GameContextCredential!, true, default));
        Assert.NotNull(await Service().GetContextAsync(unaffected.GameContextCredential!, true, default));
        _links.FindAsync("steam", "76561198000000001", Arg.Any<CancellationToken>()).Returns((ExternalIdentity?)null);
        Assert.Null(await Service().GetContextAsync(unaffected.GameContextCredential!, true, default));
    }

    private string Handoff()
    {
        string secret = GameAuthCryptography.NewToken();
        _store.Seed(RedisGameTicketStore.Key(secret), $"7|{_family:D}|0|0|production");
        return secret;
    }

    [Fact]
    public async Task Logout_revokes_authority_even_when_the_disconnect_event_is_lost()
    {
        AuthAttemptReply attempt = (await Attempt("steam"))!;
        GameAuthReply result = await Service().AuthenticateSteamAsync(attempt.AttemptCredential, "ABCD", Guid.NewGuid(), CancellationToken.None);
        GameContextRecord current = (await Service().GetContextAsync(result.GameContextCredential!, true, CancellationToken.None))!;
        Assert.True(await Service().LogoutAsync(result.GameContextCredential!, CancellationToken.None));
        Assert.Null(await Service().GetContextByIdAsync(current.Id, true, CancellationToken.None));
        Assert.Equal("CONTEXT_REVOKED", (await Service().RefreshAsync(result.GameContextRefreshToken!, Guid.NewGuid(), CancellationToken.None)).Error);
        await _revocations.Received(1).PublishAsync(new AccountId(7), current.Id);
    }

    [Fact]
    public async Task Removing_the_store_link_invalidates_a_context_without_waiting_for_its_deadline()
    {
        AuthAttemptReply attempt = (await Attempt("steam"))!;
        GameAuthReply result = await Service().AuthenticateSteamAsync(attempt.AttemptCredential, "ABCD", Guid.NewGuid(), CancellationToken.None);
        GameContextRecord current = (await Service().GetContextAsync(result.GameContextCredential!, true, CancellationToken.None))!;
        _links.FindAsync("steam", "76561198000000001", Arg.Any<CancellationToken>()).Returns((ExternalIdentity?)null);
        Assert.Null(await Service().GetContextByIdAsync(current.Id, true, CancellationToken.None));
        Assert.Null(await Service().GetContextAsync(result.GameContextCredential!, true, CancellationToken.None));
    }

    [Theory]
    [InlineData(SteamOwnershipStatus.NotOwned)]
    [InlineData(SteamOwnershipStatus.ProviderUnavailable)]
    public async Task Negative_ownership_revokes_but_an_outage_preserves_only_the_original_deadline(SteamOwnershipStatus status)
    {
        AuthAttemptReply first = (await Attempt("steam"))!;
        GameAuthReply granted = await Service().AuthenticateSteamAsync(first.AttemptCredential, "ABCD", Guid.NewGuid(), CancellationToken.None);
        GameContextRecord context = (await Service().GetContextAsync(granted.GameContextCredential!, true, CancellationToken.None))!;
        _clock.Advance(TimeSpan.FromMinutes(4));
        _ownership.CheckAsync(Arg.Any<uint>(), "76561198000000001", Arg.Any<CancellationToken>()).Returns(new SteamOwnershipResult(
            status, "76561198000000001", _clock.GetUtcNow().UtcDateTime, _clock.GetUtcNow().UtcDateTime));
        AuthAttemptReply renewal = (await Attempt("steam", granted.GameContextCredential))!;
        GameAuthReply result = await Service().AuthenticateSteamAsync(renewal.AttemptCredential, "DCBA", Guid.NewGuid(), CancellationToken.None);
        if (status == SteamOwnershipStatus.NotOwned)
        {
            Assert.Equal("pending_license", result.State);
            Assert.Null(await Service().GetContextByIdAsync(context.Id, true, CancellationToken.None));
            await _revocations.Received(1).PublishAsync(new AccountId(7), Guid.Empty);
        }
        else
        {
            Assert.Equal(granted.AuthorizationValidUntil, result.AuthorizationValidUntil);
            Assert.NotNull(await Service().GetContextByIdAsync(context.Id, true, CancellationToken.None));
            _clock.Advance(TimeSpan.FromMinutes(1));
            Assert.Null(await Service().GetContextByIdAsync(context.Id, true, CancellationToken.None));
            await _revocations.DidNotReceiveWithAnyArgs().PublishAsync(default!, default);
        }
    }

    [Fact]
    public async Task A_known_negative_observation_invalidates_another_preexisting_context()
    {
        AuthAttemptReply attempt = (await Attempt("steam"))!;
        GameAuthReply result = await Service().AuthenticateSteamAsync(attempt.AttemptCredential, "ABCD", Guid.NewGuid(), CancellationToken.None);
        GameContextRecord current = (await Service().GetContextAsync(result.GameContextCredential!, true, CancellationToken.None))!;
        await TestGameAuthorization.Licenses(_store).ApplyDecisionAsync(current.LicenseId!.Value, current.LicenseRevision!.Value,
            new(false, _clock.GetUtcNow().UtcDateTime, _clock.GetUtcNow().UtcDateTime));
        Assert.Null(await Service().GetContextByIdAsync(current.Id, true, CancellationToken.None));
        Assert.Equal("CONTEXT_REVOKED", (await Service().RefreshAsync(result.GameContextRefreshToken!, Guid.NewGuid(), CancellationToken.None)).Error);
    }

    [Fact]
    public async Task Consume_handoff_once_return_pending_license_and_recover_an_exact_response_retry()
    {
        AuthAttemptReply attempt = (await Attempt("avalon"))!;
        string ticket = Handoff();
        var request = Guid.NewGuid();
        GameAuthReply result = await Service().RedeemHandoffAsync(attempt.AttemptCredential, ticket, request, CancellationToken.None);
        Assert.Null(result.Error);
        Assert.Equal("pending_license", result.State);
        Assert.Equal("7", result.AccountId);
        Assert.NotNull(result.GameContextCredential);
        Assert.Equal(result, await Service().RedeemHandoffAsync(attempt.AttemptCredential, ticket, request, CancellationToken.None));
        Assert.Equal("INVALID_ATTEMPT", (await Service().RedeemHandoffAsync(attempt.AttemptCredential, ticket, Guid.NewGuid(), CancellationToken.None)).Error);
        AuthAttemptReply another = (await Attempt("avalon"))!;
        Assert.NotNull((await Service().RedeemHandoffAsync(another.AttemptCredential, ticket, Guid.NewGuid(), CancellationToken.None)).Error);
        Assert.DoesNotContain(ticket, string.Join("|", _store.Entries.Values), StringComparison.Ordinal);
        Assert.DoesNotContain(result.GameContextCredential!, string.Join("|", _store.Entries.Values), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rotate_without_granting_a_license_and_revoke_on_refresh_reuse()
    {
        AuthAttemptReply attempt = (await Attempt("avalon"))!;
        GameAuthReply initial = await Service().RedeemHandoffAsync(attempt.AttemptCredential, Handoff(), Guid.NewGuid(), CancellationToken.None);
        var request = Guid.NewGuid();
        GameAuthReply rotated = await Service().RefreshAsync(initial.GameContextRefreshToken!, request, CancellationToken.None);
        Assert.Equal("pending_license", rotated.State);
        Assert.Null(rotated.AuthorizationValidUntil);
        Assert.NotEqual(initial.GameContextCredential, rotated.GameContextCredential);
        Assert.Null(await Service().GetContextAsync(initial.GameContextCredential!, false, CancellationToken.None));
        Assert.Equal(rotated, await Service().RefreshAsync(initial.GameContextRefreshToken!, request, CancellationToken.None));
        Assert.Equal("REFRESH_REUSE", (await Service().RefreshAsync(initial.GameContextRefreshToken!, Guid.NewGuid(), CancellationToken.None)).Error);
        Assert.Null(await Service().GetContextAsync(rotated.GameContextCredential!, false, CancellationToken.None));
    }

    [Fact]
    public async Task Recover_a_rotation_that_commits_between_refresh_token_and_context_reads()
    {
        AuthAttemptReply attempt = (await Attempt("steam"))!;
        GameAuthReply initial = await Service().AuthenticateSteamAsync(attempt.AttemptCredential, "ABCD", Guid.NewGuid(), CancellationToken.None);
        var interleaving = new InterleavingAuthStore(_store);
        GameAuthorizationService service = Service(interleaving);
        var request = Guid.NewGuid();
        GameAuthReply? winner = null;
        interleaving.PauseKey = Avalon.Infrastructure.CacheKeys.GameAuth("production", "token", GameAuthCryptography.Digest(initial.GameContextRefreshToken!));
        interleaving.AfterRead = async () => winner = await service.RefreshAsync(initial.GameContextRefreshToken!, request, CancellationToken.None);
        GameAuthReply loser = await service.RefreshAsync(initial.GameContextRefreshToken!, request, CancellationToken.None);
        Assert.Null(loser.Error);
        Assert.Equal(winner, loser);
    }

    [Fact]
    public async Task Bind_store_proof_to_the_launcher_account_and_reject_a_different_link()
    {
        AuthAttemptReply attempt = (await Attempt("avalon"))!;
        GameAuthReply context = await Service().RedeemHandoffAsync(attempt.AttemptCredential, Handoff(), Guid.NewGuid(), CancellationToken.None);
        AuthAttemptReply proofAttempt = (await Attempt("steam", context.GameContextCredential))!;
        _links.FindAsync("steam", "76561198000000001", Arg.Any<CancellationToken>()).Returns(new ExternalIdentity
        {
            Id = Guid.NewGuid(),
            AccountId = new AccountId(8),
            Provider = "steam",
            ProviderSubject = "76561198000000001",
        });
        Assert.Equal("ACCOUNT_MISMATCH", (await Service().AuthenticateSteamAsync(proofAttempt.AttemptCredential, "ABCD", Guid.NewGuid(), CancellationToken.None)).Error);
        Assert.Null(await Service().GetContextAsync(context.GameContextCredential!, true, CancellationToken.None));
    }

    [Fact]
    public async Task Create_one_authorized_context_and_keep_the_original_license_deadline_on_refresh()
    {
        AuthAttemptReply attempt = (await Attempt("steam"))!;
        Assert.Matches("^pm[a-z2-7]{26}$", attempt.ExpectedSteamIdentity);
        var request = Guid.NewGuid();
        GameAuthReply result = await Service().AuthenticateSteamAsync(attempt.AttemptCredential, "ABCD", request, CancellationToken.None);
        Assert.Equal("authorized", result.State);
        Assert.Equal("7", result.AccountId);
        Assert.Equal(result, await Service().AuthenticateSteamAsync(attempt.AttemptCredential, "ABCD", request, CancellationToken.None));
        _clock.Advance(TimeSpan.FromMinutes(4));
        GameAuthReply refreshed = await Service().RefreshAsync(result.GameContextRefreshToken!, Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(result.AuthorizationValidUntil, refreshed.AuthorizationValidUntil);
        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Null(await Service().GetContextAsync(refreshed.GameContextCredential!, true, CancellationToken.None));
    }

    [Fact]
    public async Task Keep_an_unknown_store_identity_pending_when_it_is_bound_to_a_launcher_account()
    {
        _links.FindAsync("steam", "76561198000000001", Arg.Any<CancellationToken>()).Returns((ExternalIdentity?)null);
        AuthAttemptReply handoffAttempt = (await Attempt("avalon"))!;
        GameAuthReply inherited = await Service().RedeemHandoffAsync(handoffAttempt.AttemptCredential, Handoff(), Guid.NewGuid(), CancellationToken.None);
        AuthAttemptReply attempt = (await Attempt("steam", inherited.GameContextCredential))!;
        GameAuthReply result = await Service().AuthenticateSteamAsync(attempt.AttemptCredential, "ABCD", Guid.NewGuid(), CancellationToken.None);
        Assert.Equal("pending_link", result.State);
        Assert.Equal("ACCOUNT_LINK_REQUIRED", result.Error);
        Assert.Equal("7", result.AccountId);
        Assert.NotNull(result.PendingLinkId);
        await _accounts.DidNotReceive().CreateAsync(Arg.Any<Account>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reject_bans_session_epoch_changes_and_absolute_context_expiry()
    {
        AuthAttemptReply attempt = (await Attempt("steam"))!;
        GameAuthReply result = await Service().AuthenticateSteamAsync(attempt.AttemptCredential, "ABCD", Guid.NewGuid(), CancellationToken.None);
        _account.SessionEpoch++;
        Assert.Null(await Service().GetContextAsync(result.GameContextCredential!, false, CancellationToken.None));
        _account.SessionEpoch--;
        _account.Status = AccountStatus.Banned;
        Assert.Null(await Service().GetContextAsync(result.GameContextCredential!, false, CancellationToken.None));
        _account.Status = AccountStatus.Active;
        _clock.Advance(TimeSpan.FromHours(12));
        Assert.NotNull((await Service().RefreshAsync(result.GameContextRefreshToken!, Guid.NewGuid(), CancellationToken.None)).Error);
    }
}

internal sealed class InterleavingAuthStore(IGameContextStore inner) : IGameContextStore
{
    public string? PauseKey { get; set; }
    public Func<Task>? AfterRead { get; set; }
    public async Task<string?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        string? result = await inner.ReadAsync(key, cancellationToken);
        if (key == PauseKey && AfterRead is { } action)
        {
            AfterRead = null;
            await action();
        }
        return result;
    }
    public Task<bool> CompareExchangeAsync(IReadOnlyList<GameAuthMutation> mutations, CancellationToken cancellationToken) =>
        inner.CompareExchangeAsync(mutations, cancellationToken);
}

internal sealed class AtomicAuthStore : IGameContextStore
{
    private readonly object _gate = new();
    public Dictionary<string, string> Entries { get; } = new(StringComparer.Ordinal);
    public void Seed(string key, string value) { lock (_gate) Entries[key] = value; }
    public Task<string?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult(Entries.TryGetValue(key, out string? value) ? value : null);
    }
    public Task<bool> CompareExchangeAsync(IReadOnlyList<GameAuthMutation> mutations, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (mutations.Any(m => (Entries.TryGetValue(m.Key, out string? current) ? current : null) != m.Expected))
                return Task.FromResult(false);
            foreach (GameAuthMutation mutation in mutations)
                if (mutation.Value is null) Entries.Remove(mutation.Key); else Entries[mutation.Key] = mutation.Value;
            return Task.FromResult(true);
        }
    }
}
