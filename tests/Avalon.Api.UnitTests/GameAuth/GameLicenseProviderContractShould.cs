using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.GameAuth;
using Avalon.Infrastructure.StoreAuth;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.GameAuth;

public sealed class GameLicenseProviderContractShould
{
    private static readonly DateTime Now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("steam", LicenseAuthorityKind.VerifiedOwnership)]
    [InlineData("avalon", LicenseAuthorityKind.StoredGrant)]
    [InlineData("test-store", LicenseAuthorityKind.VerifiedOwnership)]
    public async Task Providers_use_the_same_bound_license_and_capped_authority(string provider, LicenseAuthorityKind kind)
    {
        var fixture = new Fixture(provider, kind);
        var result = await fixture.Service.VerifyAsync(fixture.Request, default);
        Assert.Equal(GameLicenseCheckStatus.Licensed, result.Status);
        Assert.Equal(fixture.License.Id, result.LicenseId);
        Assert.Equal(1, result.Revision);
        Assert.Equal(Now.AddMinutes(5), result.AuthorizedUntil);
        Assert.True(await fixture.Service.ValidateAsync(result.LicenseId!.Value, 1, fixture.Request.Account, fixture.Application, Now, default));
        Assert.False(await fixture.Service.ValidateAsync(result.LicenseId.Value, 1, new AccountId(8), fixture.Application, Now, default));
        Assert.False(await fixture.Service.ValidateAsync(result.LicenseId.Value, 2, fixture.Request.Account, fixture.Application, Now, default));
        var observation = fixture.Observations.ReceivedCalls().Single().GetArguments()[0] as LicenseObservation;
        Assert.Equal(result.LicenseId, observation!.LicenseId);
        Assert.Equal(result.Revision, observation.AuthorityRevision);
        Assert.Equal(provider, observation.Provider);
    }

    [Fact]
    public async Task Short_source_expiry_and_identity_deadline_bound_authority()
    {
        var fixture = new Fixture("test-store", LicenseAuthorityKind.VerifiedOwnership);
        fixture.Provider.Result = fixture.Provider.Result with { ProviderExpiresAt = Now.AddSeconds(20) };
        var result = await fixture.Service.VerifyAsync(fixture.Request, default);
        Assert.Equal(Now.AddSeconds(20), result.AuthorizedUntil);
        fixture.Provider.Result = fixture.Provider.Result with { ProviderExpiresAt = null };
        result = await fixture.Service.VerifyAsync(fixture.Request with
        { Identity = fixture.Request.Identity! with { ValidUntil = Now.AddSeconds(10) } }, default);
        Assert.Equal(Now.AddSeconds(10), result.AuthorizedUntil);
    }

    [Fact]
    public async Task Unavailable_or_forged_evidence_does_not_extend_persisted_authority()
    {
        var fixture = new Fixture("test-store", LicenseAuthorityKind.VerifiedOwnership);
        foreach (var bad in new[]
        {
            fixture.Provider.Result with { Status = GameLicenseCheckStatus.Unavailable },
            fixture.Provider.Result with { ProviderSubject = "other" },
            fixture.Provider.Result with { ProviderProductId = "other" },
            fixture.Provider.Result with { ObservedAt = Now.AddSeconds(1) },
            fixture.Provider.Result with { LicenseId = Guid.NewGuid() },
        })
        {
            fixture.Provider.Result = bad;
            Assert.Equal(GameLicenseCheckStatus.Unavailable, (await fixture.Service.VerifyAsync(fixture.Request, default)).Status);
        }
        await fixture.Licenses.DidNotReceiveWithAnyArgs().ApplyDecisionAsync(default, default, default!, default);
        await fixture.Observations.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
    }

    [Fact]
    public async Task Unknown_provider_and_unconfigured_application_fail_closed()
    {
        var fixture = new Fixture("test-store", LicenseAuthorityKind.VerifiedOwnership);
        Assert.Equal(GameLicenseCheckStatus.Unavailable, (await fixture.Service.VerifyAsync(fixture.Request with
        { Application = new("unknown", "unknown", "base", "avalon.base", "production", [], false) }, default)).Status);
        Assert.Equal(GameLicenseCheckStatus.Unavailable, (await fixture.Service.VerifyAsync(fixture.Request with
        { Application = new(fixture.Application.Key, "test-store", "other", "avalon.base", "production", [], false) }, default)).Status);
    }

    [Fact]
    public async Task Negative_evidence_invalidates_exact_revision_and_a_bound_context_cannot_reestablish_it()
    {
        var fixture = new Fixture("test-store", LicenseAuthorityKind.VerifiedOwnership);
        fixture.Provider.Result = fixture.Provider.Result with { Status = GameLicenseCheckStatus.Unlicensed, AuthorizedUntil = Now };
        Assert.Equal(GameLicenseCheckStatus.Unlicensed, (await fixture.Service.VerifyAsync(fixture.Request with
        { BoundLicenseId = fixture.License.Id, BoundRevision = 1 }, default)).Status);
        Assert.Equal(2, fixture.License.AuthorityRevision);
        Assert.False(await fixture.Service.ValidateAsync(fixture.License.Id, 1, fixture.Request.Account, fixture.Application, Now, default));
        fixture.Provider.Result = fixture.Provider.Result with { Status = GameLicenseCheckStatus.Licensed, AuthorizedUntil = Now.AddMinutes(5), ObservedAt = Now.AddSeconds(1) };
        Assert.Equal(GameLicenseCheckStatus.Unlicensed, (await fixture.Service.VerifyAsync(fixture.Request with
        { BoundLicenseId = fixture.License.Id, BoundRevision = 1, Now = Now.AddSeconds(1) }, default)).Status);
    }

    private sealed class Fixture
    {
        public GameApplicationSelection Application { get; }
        public GameLicenseCheckRequest Request { get; }
        public GameLicense License { get; }
        public IGameLicenseRepository Licenses { get; } = Substitute.For<IGameLicenseRepository>();
        public ILicenseObservationRepository Observations { get; } = Substitute.For<ILicenseObservationRepository>();
        public FakeProvider Provider { get; }
        public GameLicenseAuthorityService Service { get; }
        public Fixture(string provider, LicenseAuthorityKind kind)
        {
            var config = new StoreAuthenticationConfiguration { SteamAppId = 2499460 };
            var key = provider == "steam" ? "steam.main" : provider == "avalon" ? "avalon.base" : "test-store.main";
            var providerProduct = provider == "steam" ? "2499460" : "base";
            if (provider == "test-store") config.AdditionalApplications[key] = new() { Provider = provider, ProviderProductId = providerProduct };
            Application = config.ResolveApplication(key)!;
            Request = new(new AccountId(7), Application, kind == LicenseAuthorityKind.StoredGrant ? null : new("subject", Now, Now.AddMinutes(30)), null, null, Now);
            License = new()
            {
                Id = Guid.NewGuid(), AccountId = Request.Account, Provider = provider, ProviderSubject = Request.Identity?.ProviderSubject,
                Product = "avalon.base", Environment = "production", ProviderProductId = providerProduct, LicenseReference = "same-reference",
                AuthorityKind = kind, GrantedAt = Now.AddDays(-1), AuthorityRevision = 1,
            };
            Licenses.FindAsync(License.Id, Arg.Any<CancellationToken>()).Returns(_ => License);
            Licenses.FindAsync(Request.Account, provider, "production", "same-reference", Arg.Any<CancellationToken>()).Returns(_ => License);
            Licenses.ApplyDecisionAsync(License.Id, Arg.Any<long>(), Arg.Any<LicenseAuthorityDecision>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                if (call.ArgAt<long>(1) != License.AuthorityRevision) return null;
                var decision = call.ArgAt<LicenseAuthorityDecision>(2);
                if (!decision.OwnsProduct) { License.AuthorityRevision++; License.RevokedAt = decision.ObservedAt; }
                License.LastObservedAt = decision.ObservedAt;
                License.VerifiedUntil = decision.OwnsProduct ? decision.AuthorizedUntil : null;
                return License;
            });
            Provider = new(provider, kind, new(GameLicenseCheckStatus.Licensed, "same-reference", Now, Now.AddHours(1),
                ProviderProductId: providerProduct, ProviderSubject: Request.Identity?.ProviderSubject));
            Service = new(new GameProviderRegistry([], [Provider]), Licenses, Observations, Options.Create(config));
        }
    }

    private sealed class FakeProvider(string provider, LicenseAuthorityKind kind, GameLicenseCheckResult result) : IGameLicenseProvider
    {
        public string Provider => provider;
        public LicenseAuthorityKind AuthorityKind => kind;
        public GameLicenseCheckResult Result { get; set; } = result;
        public Task<GameLicenseCheckResult> CheckAsync(GameLicenseCheckRequest request, CancellationToken ct) => Task.FromResult(Result);
    }
}
