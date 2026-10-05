using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Infrastructure.StoreAuth;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.StoreAuth;

public sealed class GameSteamAdaptersShould
{
    [Fact]
    public async Task Steam_transports_are_bound_to_the_configured_application_and_verified_subject()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));
        var now = clock.GetUtcNow().UtcDateTime;
        var config = Options.Create(new StoreAuthenticationConfiguration
        { SteamAppId = 2499460, SteamPlaytest = new() { Enabled = true, AppId = 2514590, AllowedWorldIds = [3] } });
        var application = config.Value.ResolveApplication("steam.playtest")!;
        var verifier = Substitute.For<ISteamProofVerifier>();
        verifier.VerifyAsync(2514590, "ABCD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SteamProofResult(SteamProofStatus.Verified, "subject"));
        var identityProvider = new SteamIdentityProvider(verifier, config, clock);
        var challenge = identityProvider.CreateChallenge(application);
        Assert.StartsWith("pt", challenge);
        var identity = await identityProvider.VerifyAsync(new(application, challenge, "ABCD"), default);
        Assert.Equal(GameIdentityProofStatus.Verified, identity.Status);
        Assert.Equal(now.AddMinutes(30), identity.Identity!.ValidUntil);
        var ownership = Substitute.For<ISteamOwnershipClient>();
        ownership.CheckAsync(Arg.Any<uint>(), "subject", Arg.Any<CancellationToken>()).Returns(
            new SteamOwnershipResult(SteamOwnershipStatus.Owned, "subject", now, now.AddMinutes(5), OwnerSubject: "owner"));
        var provider = new SteamLicenseProvider(ownership);
        var request = new GameLicenseCheckRequest(new AccountId(7), application, identity.Identity, null, null, now);
        var license = await provider.CheckAsync(request, default);
        Assert.Equal(GameLicenseCheckStatus.Licensed, license.Status);
        Assert.Equal("subject", license.ProviderSubject);
        Assert.Equal("owner", license.OwnerSubject);
        Assert.Equal("2514590", license.ProviderProductId);
        Assert.Equal(LicenseAuthorityKind.VerifiedOwnership, provider.AuthorityKind);
        var retry = await provider.CheckAsync(request, default);
        Assert.Equal(license.LicenseReference, retry.LicenseReference);
        Assert.NotEqual(license.LicenseReference, (await provider.CheckAsync(request with
        { Application = config.Value.ResolveApplication("steam.main")! }, default)).LicenseReference);
        await verifier.Received(1).VerifyAsync(2514590, "ABCD", challenge, Arg.Any<CancellationToken>());
    }
}
