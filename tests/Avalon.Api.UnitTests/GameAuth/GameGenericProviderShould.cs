using Avalon.Common.GameAuth;
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

public sealed class GameGenericProviderShould
{
    [Fact]
    public async Task Registered_future_store_uses_the_same_context_and_license_orchestration()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));
        IOptions<StoreAuthenticationConfiguration> config = Options.Create(new StoreAuthenticationConfiguration { SteamAppId = 2499460 });
        config.Value.AdditionalApplications["future.main"] = new() { Provider = "test-store", ProviderProductId = "base" };
        var provider = new Provider(clock);
        var registry = new GameProviderRegistry([provider], [provider]);
        var licenses = new MemoryGameLicenses();
        var account = new Account { Id = new(7), Username = "PLAYER", Email = "player@example.test", Salt = [1], Verifier = [2], JoinDate = DateTime.UnixEpoch };
        IAccountRepository accounts = Substitute.For<IAccountRepository>();
        accounts.FindByIdAsync(account.Id, false, Arg.Any<CancellationToken>()).Returns(account);
        IExternalIdentityRepository links = Substitute.For<IExternalIdentityRepository>();
        links.FindAsync("test-store", "subject", Arg.Any<CancellationToken>()).Returns(new ExternalIdentity
        { Id = Guid.NewGuid(), AccountId = account.Id, Provider = "test-store", ProviderSubject = "subject" });
        var store = new AtomicAuthStore();
        var crypto = new GameAuthCryptography(new byte[32]);
        var service = new GameAuthorizationService(store, new(store, crypto, config, clock), crypto, accounts,
            Substitute.For<IRefreshTokenRepository>(), links, registry, new(registry, licenses, Substitute.For<ILicenseObservationRepository>(), config, clock), config, clock);
        AuthAttemptReply attempt = (await service.CreateProviderAttemptAsync("future.main", "1", Guid.NewGuid(), new string('A', 43), null, 0, default))!;
        Assert.Equal("future-challenge", attempt.ExpectedSteamIdentity);
        GameAuthReply result = await service.AuthenticateProviderAsync("test-store", attempt.AttemptCredential, "opaque-proof", Guid.NewGuid(), default);
        Assert.Equal("authorized", result.State);
        Assert.Equal("test-store", result.LicenseSource);
        GameContextRecord context = (await service.GetContextAsync(result.GameContextCredential!, true, default))!;
        Assert.Equal("future.main", context.ApplicationKey);
        Assert.Equal(context.LicenseId, Assert.Single(licenses.Rows).Id);
        GameAuthReply refreshed = await service.RefreshAsync(result.GameContextRefreshToken!, Guid.NewGuid(), default);
        Assert.Equal(result.AuthorizationValidUntil, refreshed.AuthorizationValidUntil);
        IGameSessionRepository sessions = Substitute.For<IGameSessionRepository>();
        IGameServerAllocator allocator = Substitute.For<IGameServerAllocator>();
        allocator.FindAsync(Arg.Any<GameContextRecord>(), 1, null, Arg.Any<CancellationToken>())
            .Returns(new GameWorldDestination(1, "world-1", "Avalon", "localhost", 21000, "localhost", new string('A', 64), "1", "1"));
        sessions.TryReserveAsync(Arg.Any<GameSessionReservation>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            GameSessionReservation reservation = call.Arg<GameSessionReservation>();
            return new GameSession
            {
                AccountId = reservation.AccountId,
                GameSessionId = reservation.GameSessionId,
                GameContextId = reservation.GameContextId,
                FencingToken = 1,
                ServerId = "world-1",
                WorldId = 1,
                Environment = "production",
                CreatedAt = clock.GetUtcNow().UtcDateTime,
                LeaseUntil = clock.GetUtcNow().UtcDateTime.AddSeconds(30),
                LicenseUntil = reservation.LicenseUntil
            };
        });
        var tickets = new JoinTicketStore(store, crypto, service, sessions, allocator, config, clock, new(config));
        GameJoinReply issued = await tickets.IssueAsync(refreshed.GameContextCredential!, 1, null, Guid.NewGuid(), false, false, default);
        Assert.Null(issued.Error);
        Assert.Null((await tickets.RedeemAsync(issued.JoinTicket!, "world-1", Guid.NewGuid(), Guid.NewGuid(), default)).Error);
    }
    private sealed class Provider(TimeProvider clock) : IGameIdentityProvider, IGameLicenseProvider
    {
        string IGameIdentityProvider.Provider => "test-store";
        string IGameLicenseProvider.Provider => "test-store";
        public LicenseAuthorityKind AuthorityKind => LicenseAuthorityKind.VerifiedOwnership;
        private DateTime Now => clock.GetUtcNow().UtcDateTime;
        public string CreateChallenge(GameApplicationSelection application) => "future-challenge";
        public Task<GameIdentityProofResult> VerifyAsync(GameIdentityProofRequest request, CancellationToken ct) =>
            Task.FromResult(request.ExpectedChallenge == "future-challenge" && request.Proof == "opaque-proof"
                ? new GameIdentityProofResult(GameIdentityProofStatus.Verified, new("subject", Now, Now.AddMinutes(30)))
                : new(GameIdentityProofStatus.Invalid));
        public Task<GameLicenseCheckResult> CheckAsync(GameLicenseCheckRequest request, CancellationToken ct) =>
            Task.FromResult(new GameLicenseCheckResult(GameLicenseCheckStatus.Licensed, "ownership:base:subject", Now, Now.AddMinutes(5),
                ProviderProductId: "base", ProviderSubject: "subject"));
    }
}
