using System.Security.Cryptography;
using System.Text;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.GameAuth;
using Avalon.Infrastructure.GameTickets;
using Avalon.Infrastructure.StoreAuth;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.GameAuth;

public class PendingLinkStoreShould
{
    private const string Subject = "76561198000000001";
    private readonly string _verifier = new('V', 43);
    private readonly AtomicAuthStore _store = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero));
    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly IExternalIdentityRepository _identities = Substitute.For<IExternalIdentityRepository>();
    private readonly IMfaSetupRepository _mfa = Substitute.For<IMfaSetupRepository>();
    private readonly IRefreshTokenRepository _families = Substitute.For<IRefreshTokenRepository>();
    private readonly IGameAccountRegistration _registration = Substitute.For<IGameAccountRegistration>();
    private readonly Guid _family = Guid.NewGuid();
    private readonly ISteamOwnershipClient _ownership = Substitute.For<ISteamOwnershipClient>();
    private readonly Account _account = new() { Id = new AccountId(7), Username = "PLAYER", Email = "player@example.test", Salt = [1], Verifier = [2], JoinDate = DateTime.UnixEpoch };
    private readonly GameAuthorizationService _auth;
    private readonly PendingLinkStore _links;
    private readonly StoreAuthenticationConfiguration _configuration = new() { SteamAppId = StoreAuthenticationTestData.SteamAppId, SteamPublisherKey = "test-secret", SteamPlaytest = new() { Enabled = true, AppId = 2514590, AllowedWorldIds = [3] } };

    public PendingLinkStoreShould()
    {
        _families.IsLiveLauncherFamilyAsync(Arg.Any<AccountId>(), _family, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
        var crypto = new GameAuthCryptography(Enumerable.Range(1, 32).Select(x => (byte)x).ToArray());
        IOptions<StoreAuthenticationConfiguration> options = Options.Create(_configuration);
        ISteamProofVerifier proof = Substitute.For<ISteamProofVerifier>();
        proof.VerifyAsync(Arg.Any<uint>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new SteamProofResult(SteamProofStatus.Verified, Subject));
        _accounts.FindByIdAsync(_account.Id, false, Arg.Any<CancellationToken>()).Returns(_account);
        _ownership.CheckAsync(Arg.Any<uint>(), Subject, Arg.Any<CancellationToken>()).Returns(_ => new SteamOwnershipResult(SteamOwnershipStatus.Owned, Subject, _clock.GetUtcNow().UtcDateTime, _clock.GetUtcNow().UtcDateTime.AddMinutes(5)));
        _auth = TestGameAuthorization.Create(_store, new AuthAttemptStore(_store, crypto, options, _clock), crypto, _accounts,
            _families, _identities, Substitute.For<ILicenseObservationRepository>(), proof, _ownership, options, _clock, _registration);
        _links = new(_auth, _store, crypto, _accounts, _mfa, options, _clock);
        _identities.LinkWithAuthorityAsync(Arg.Any<IdentityLinkOperation>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            IdentityLinkOperation operation = call.Arg<IdentityLinkOperation>();
            _account.SessionEpoch = operation.SessionEpoch + 1;
            var identity = new ExternalIdentity { Id = operation.OperationId, AccountId = _account.Id, Provider = "steam", ProviderSubject = Subject };
            _identities.FindAsync("steam", Subject, Arg.Any<CancellationToken>()).Returns(identity);
            return new IdentityLinkResult(IdentityLinkStatus.Linked, identity);
        });
    }

    private async Task<GameAuthReply> Pending()
    {
        var runId = Guid.NewGuid();
        string challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(_verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        AuthAttemptReply handoffAttempt = (await _auth.CreateAttemptAsync("avalon", "1", runId, challenge, null, null, CancellationToken.None))!;
        string handoff = GameAuthCryptography.NewToken();
        _store.Seed(RedisGameTicketStore.Key(handoff), $"7|{_family:D}|0|0|production");
        GameAuthReply inherited = await _auth.RedeemHandoffAsync(handoffAttempt.AttemptCredential, handoff, Guid.NewGuid(), CancellationToken.None);
        AuthAttemptReply attempt = (await _auth.CreateAttemptAsync("steam", "1", runId, challenge, inherited.GameContextCredential, null, CancellationToken.None))!;
        return await _auth.AuthenticateSteamAsync(attempt.AttemptCredential, "ABCD", Guid.NewGuid(), CancellationToken.None);
    }

    [Fact]
    public async Task Linking_preserves_playtest_application()
    {
        GameAuthReply pending = await Pending();
        GameContextRecord context = (await _auth.GetContextAsync(pending.GameContextCredential!, false, default))!;
        // A pending Playtest context is a server-owned fixture, independent of launcher authority.
        _store.Seed(Avalon.Infrastructure.CacheKeys.GameAuth("production", "context", context.Id.ToString("N")), GameAuthJson.Serialize(context with { SteamAppId = 2514590, ApplicationKey = "steam.playtest" }));
        var id = Guid.ParseExact(pending.PendingLinkId!, "N");
        await _links.ConfirmAsync(id, _account.Id, 0, 0, null, Guid.NewGuid(), default);
        LinkProposalReply proposal = await _links.ProposalAsync(pending.GameContextCredential!, _verifier, default);
        GameAuthReply result = await _auth.CompleteAccountLinkAsync(_links, pending.GameContextCredential!, proposal.ConsentCode!, _verifier, Guid.NewGuid(), true, default);
        Assert.Equal("authorized", result.State);
        Assert.Equal(2514590u, (await _auth.GetContextAsync(result.GameContextCredential!, true, default))!.SteamAppId);
        await _ownership.Received(1).CheckAsync(2514590, Subject, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Require_browser_consent_and_game_acceptance_before_linking_or_checking_a_license()
    {
        GameAuthReply pending = await Pending();
        var id = Guid.ParseExact(pending.PendingLinkId!, "N");
        Assert.Equal("pending", (await _links.ProposalAsync(pending.GameContextCredential!, _verifier, CancellationToken.None)).State);
        LinkBrowserReply browser = await _links.ConfirmAsync(id, _account.Id, 0, 0, null, Guid.NewGuid(), CancellationToken.None);
        Assert.Equal("awaiting_game_confirmation", browser.State);
        await _identities.DidNotReceive().LinkWithAuthorityAsync(Arg.Any<IdentityLinkOperation>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _ownership.DidNotReceive().CheckAsync(Arg.Any<uint>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        LinkProposalReply proposal = await _links.ProposalAsync(pending.GameContextCredential!, _verifier, CancellationToken.None);
        Assert.Equal("7", proposal.AccountId);
        Assert.Equal("PLAYER", proposal.Username);
        Assert.NotNull(proposal.ConsentCode);
        Assert.DoesNotContain(proposal.ConsentCode!, string.Join("|", _store.Entries.Values), StringComparison.Ordinal);
        var request = Guid.NewGuid();
        Assert.NotNull((await _auth.CompleteAccountLinkAsync(_links, pending.GameContextCredential!, proposal.ConsentCode!, _verifier, request, false, CancellationToken.None)).Error);
        GameAuthReply linked = await _auth.CompleteAccountLinkAsync(_links, pending.GameContextCredential!, proposal.ConsentCode!, _verifier, request, true, CancellationToken.None);
        Assert.Equal("authorized", linked.State);
        Assert.Equal("7", linked.AccountId);
        Assert.Equal(1, _account.SessionEpoch);
        Assert.Equal(linked, await _auth.CompleteAccountLinkAsync(_links, pending.GameContextCredential!, proposal.ConsentCode!, _verifier, request, true, CancellationToken.None));
        Assert.NotNull((await _auth.CompleteAccountLinkAsync(_links, pending.GameContextCredential!, proposal.ConsentCode!, _verifier, Guid.NewGuid(), true, CancellationToken.None)).Error);
        Assert.NotNull(await _auth.GetContextAsync(linked.GameContextCredential!, true, CancellationToken.None));
    }

    [Fact]
    public async Task Reject_wrong_PKCE_missing_game_secret_and_expired_proof()
    {
        GameAuthReply pending = await Pending();
        var id = Guid.ParseExact(pending.PendingLinkId!, "N");
        await _links.ConfirmAsync(id, _account.Id, 0, 0, null, Guid.NewGuid(), CancellationToken.None);
        Assert.NotNull((await _links.ProposalAsync(pending.GameContextCredential!, new string('X', 43), CancellationToken.None)).Error);
        Assert.NotNull((await _links.ProposalAsync(id.ToString("N"), _verifier, CancellationToken.None)).Error);
        LinkProposalReply proposal = await _links.ProposalAsync(pending.GameContextCredential!, _verifier, CancellationToken.None);
        Assert.NotNull((await _auth.CompleteAccountLinkAsync(_links, GameAuthCryptography.NewToken(), proposal.ConsentCode!, _verifier, Guid.NewGuid(), true, CancellationToken.None)).Error);
        _clock.Advance(TimeSpan.FromMinutes(5));
        Assert.NotNull((await _auth.CompleteAccountLinkAsync(_links, pending.GameContextCredential!, proposal.ConsentCode!, _verifier, Guid.NewGuid(), true, CancellationToken.None)).Error);
        await _identities.DidNotReceive().LinkWithAuthorityAsync(Arg.Any<IdentityLinkOperation>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Require_recent_current_account_and_MFA_snapshot_for_browser_consent()
    {
        GameAuthReply pending = await Pending();
        var id = Guid.ParseExact(pending.PendingLinkId!, "N");
        Assert.NotNull((await _links.ConfirmAsync(id, _account.Id, 1, 0, null, Guid.NewGuid(), CancellationToken.None)).Error);
        Assert.NotNull((await _links.ConfirmAsync(id, _account.Id, 0, 1, null, Guid.NewGuid(), CancellationToken.None)).Error);
        _mfa.FindByAccountIdAsync(_account.Id, Arg.Any<CancellationToken>()).Returns(new MFASetup { Id = Guid.NewGuid(), AccountId = _account.Id, Account = _account, Secret = [1], Status = MfaSetupStatus.Confirmed });
        Assert.NotNull((await _links.ConfirmAsync(id, _account.Id, 0, 0, null, Guid.NewGuid(), CancellationToken.None)).Error);
    }

    [Fact]
    public async Task Refuse_link_completion_after_the_inherited_launcher_family_is_revoked()
    {
        _families.IsLiveLauncherFamilyAsync(_account.Id, _family, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
        var run = Guid.NewGuid();
        string challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(_verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        AuthAttemptReply attempt = (await _auth.CreateAttemptAsync("avalon", "1", run, challenge, null, null, CancellationToken.None))!;
        string handoff = GameAuthCryptography.NewToken();
        _store.Seed(RedisGameTicketStore.Key(handoff), $"7|{_family:D}|0|0|production");
        GameAuthReply restricted = await _auth.RedeemHandoffAsync(attempt.AttemptCredential, handoff, Guid.NewGuid(), CancellationToken.None);
        AuthAttemptReply proofAttempt = (await _auth.CreateAttemptAsync("steam", "1", run, challenge, restricted.GameContextCredential, null, CancellationToken.None))!;
        GameAuthReply pending = await _auth.AuthenticateSteamAsync(proofAttempt.AttemptCredential, "ABCD", Guid.NewGuid(), CancellationToken.None);
        await _links.ConfirmAsync(Guid.ParseExact(pending.PendingLinkId!, "N"), _account.Id, 0, 0, null, Guid.NewGuid(), CancellationToken.None);
        LinkProposalReply proposal = await _links.ProposalAsync(pending.GameContextCredential!, _verifier, CancellationToken.None);
        _families.IsLiveLauncherFamilyAsync(_account.Id, _family, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(false);
        Assert.NotNull((await _auth.CompleteAccountLinkAsync(_links, pending.GameContextCredential!, proposal.ConsentCode!, _verifier, Guid.NewGuid(), true, CancellationToken.None)).Error);
        await _identities.DidNotReceive().LinkWithAuthorityAsync(Arg.Any<IdentityLinkOperation>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Recover_after_a_failure_between_the_database_commit_and_context_rotation()
    {
        GameAuthReply pending = await Pending();
        await _links.ConfirmAsync(Guid.ParseExact(pending.PendingLinkId!, "N"), _account.Id, 0, 0, null, Guid.NewGuid(), CancellationToken.None);
        LinkProposalReply proposal = await _links.ProposalAsync(pending.GameContextCredential!, _verifier, CancellationToken.None);
        var requestId = Guid.NewGuid();
        _ownership.CheckAsync(Arg.Any<uint>(), Subject, Arg.Any<CancellationToken>()).Returns(Task.FromException<SteamOwnershipResult>(new IOException("Simulated response loss")));
        Assert.Equal("PROVIDER_UNAVAILABLE", (await _auth.CompleteAccountLinkAsync(_links, pending.GameContextCredential!, proposal.ConsentCode!, _verifier, requestId, true, CancellationToken.None)).Error);
        Assert.Equal(1, _account.SessionEpoch);
        _clock.Advance(TimeSpan.FromSeconds(16));
        _ownership.CheckAsync(Arg.Any<uint>(), Subject, Arg.Any<CancellationToken>()).Returns(new SteamOwnershipResult(SteamOwnershipStatus.Owned, Subject, _clock.GetUtcNow().UtcDateTime, _clock.GetUtcNow().UtcDateTime.AddMinutes(5)));
        GameAuthReply result = await _auth.CompleteAccountLinkAsync(_links, pending.GameContextCredential!, proposal.ConsentCode!, _verifier, requestId, true, CancellationToken.None);
        Assert.Equal("authorized", result.State);
        Assert.Equal(1, _account.SessionEpoch);
    }

}
