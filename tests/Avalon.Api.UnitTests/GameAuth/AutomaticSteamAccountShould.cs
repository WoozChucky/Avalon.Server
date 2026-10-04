using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.GameAuth;
using Avalon.Infrastructure.StoreAuth;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.GameAuth;

public class AutomaticSteamAccountShould
{
    private const string Subject = "76561198000000001";
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero));
    private readonly AtomicAuthStore _store = new();
    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly IExternalIdentityRepository _identities = Substitute.For<IExternalIdentityRepository>();
    private readonly IGameAccountRegistration _registration = Substitute.For<IGameAccountRegistration>();
    private readonly ISteamProofVerifier _proof = Substitute.For<ISteamProofVerifier>();
    private readonly ISteamOwnershipClient _ownership = Substitute.For<ISteamOwnershipClient>();
    private readonly GameAuthorizationService _service;
    private readonly Account _account = new() { Id = new AccountId(7), Username = "STEAMPLAYER", Email = null!, Salt = [], Verifier = [], JoinDate = DateTime.UnixEpoch, SessionEpoch = 1 };

    public AutomaticSteamAccountShould()
    {
        var crypto = new GameAuthCryptography(Enumerable.Range(1, 32).Select(x => (byte)x).ToArray());
        var options = Options.Create(new StoreAuthenticationConfiguration { SteamPublisherKey = "test-only" });
        _service = new(_store, new AuthAttemptStore(_store, crypto, options, _clock), crypto, _accounts,
            Substitute.For<IRefreshTokenRepository>(), _identities, Substitute.For<ILicenseObservationRepository>(), _proof,
            _ownership, options, _clock, _registration);
        _proof.VerifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new SteamProofResult(SteamProofStatus.Verified, Subject));
        _ownership.CheckAsync(Subject, Arg.Any<CancellationToken>()).Returns(new SteamOwnershipResult(SteamOwnershipStatus.Owned, Subject, _clock.GetUtcNow().UtcDateTime, _clock.GetUtcNow().UtcDateTime.AddMinutes(5)));
        _accounts.FindByIdAsync(_account.Id, false, Arg.Any<CancellationToken>()).Returns(_account);
        _registration.CreateFromSteamAsync(Arg.Any<Guid>(), Subject, Arg.Any<DateTime>(), "127.0.0.1", Arg.Any<CancellationToken>())
            .Returns(call => new IdentityLinkResult(IdentityLinkStatus.Linked, new ExternalIdentity { Id = call.ArgAt<Guid>(0), AccountId = _account.Id, Provider = "steam", ProviderSubject = Subject }));
    }

    [Fact]
    public async Task Automatically_create_and_link_after_verification_without_browser_consent_and_recover_exact_retry()
    {
        var attempt = (await _service.CreateAttemptAsync("steam", "1", Guid.NewGuid(), new string('A', 43), null, CancellationToken.None))!;
        var request = Guid.NewGuid();
        var result = await _service.AuthenticateSteamAsync(attempt.AttemptCredential, "ABCD", request, CancellationToken.None, "127.0.0.1");
        Assert.Null(result.Error);
        Assert.Equal("authorized", result.State);
        Assert.Equal("7", result.AccountId);
        Assert.Null(result.PendingLinkId);
        Assert.Equal(result, await _service.AuthenticateSteamAsync(attempt.AttemptCredential, "ABCD", request, CancellationToken.None, "127.0.0.1"));
        await _registration.Received(1).CreateFromSteamAsync(Arg.Any<Guid>(), Subject, Arg.Any<DateTime>(), "127.0.0.1", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(SteamOwnershipStatus.NotOwned)]
    [InlineData(SteamOwnershipStatus.ProviderUnavailable)]
    public async Task Never_create_an_account_without_a_fresh_verified_purchase(SteamOwnershipStatus status)
    {
        _ownership.CheckAsync(Subject, Arg.Any<CancellationToken>()).Returns(new SteamOwnershipResult(status, Subject, _clock.GetUtcNow().UtcDateTime, _clock.GetUtcNow().UtcDateTime));
        var attempt = (await _service.CreateAttemptAsync("steam", "1", Guid.NewGuid(), new string('A', 43), null, CancellationToken.None))!;
        var result = await _service.AuthenticateSteamAsync(attempt.AttemptCredential, "ABCD", Guid.NewGuid(), CancellationToken.None, "127.0.0.1");
        Assert.NotNull(result.Error);
        await _registration.DidNotReceiveWithAnyArgs().CreateFromSteamAsync(default, default!, default, default!, default);
    }

    [Fact]
    public async Task Adopt_the_single_race_winner_instead_of_creating_a_duplicate_root()
    {
        _registration.CreateFromSteamAsync(Arg.Any<Guid>(), Subject, Arg.Any<DateTime>(), "127.0.0.1", Arg.Any<CancellationToken>()).Returns(new IdentityLinkResult(IdentityLinkStatus.SubjectTaken, null));
        _identities.FindAsync("steam", Subject, Arg.Any<CancellationToken>()).Returns((ExternalIdentity?)null,
            new ExternalIdentity { Id = Guid.NewGuid(), AccountId = _account.Id, Provider = "steam", ProviderSubject = Subject });
        var attempt = (await _service.CreateAttemptAsync("steam", "1", Guid.NewGuid(), new string('A', 43), null, CancellationToken.None))!;
        var result = await _service.AuthenticateSteamAsync(attempt.AttemptCredential, "ABCD", Guid.NewGuid(), CancellationToken.None, "127.0.0.1");
        Assert.Equal("authorized", result.State);
        Assert.Equal("7", result.AccountId);
    }
}
