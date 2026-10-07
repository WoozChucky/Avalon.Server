using System.Net;
using Avalon.Infrastructure.StoreAuth;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Avalon.Api.UnitTests.StoreAuth;

public class SteamOwnershipClientShould
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(StoreAuthenticationTestData.SteamAppId)]
    [InlineData(123456)]
    public async Task Accept_a_borrowed_active_license_and_cap_at_its_provider_expiry(uint appId)
    {
        using var handler = new RecordingSteamHandler { Body = "{\"appownership\":{\"ownsapp\":true,\"permanent\":false,\"ownersteamid\":\"76561198000000002\",\"timeexpires\":\"2026-10-04T12:02:00Z\"}}" };
        using var client = new HttpClient(handler);
        SteamOwnershipResult result = await new SteamOwnershipClient(client, SteamProofVerifierShould.Configuration(appId), new FakeTimeProvider(Now))
            .CheckAsync(appId, SteamProofVerifierShould.SteamId, CancellationToken.None);
        Assert.Equal(SteamOwnershipStatus.Owned, result.Status);
        Assert.Equal(SteamProofVerifierShould.SteamId, result.ProviderSubject);
        Assert.Equal("76561198000000002", result.OwnerSubject);
        Assert.False(result.Permanent);
        Assert.Equal(Now.UtcDateTime.AddMinutes(2), result.AuthorizedUntil);
        Uri request = Assert.Single(handler.Requests);
        Assert.Contains("steamid=" + SteamProofVerifierShould.SteamId, request.Query, StringComparison.Ordinal);
        Assert.Contains("appid=" + appId.ToString(System.Globalization.CultureInfo.InvariantCulture), request.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cap_permanent_ownership_to_five_minutes()
    {
        using var handler = new RecordingSteamHandler { Body = "{\"appownership\":{\"ownsapp\":true,\"permanent\":true,\"timeexpires\":\"never\"}}" };
        using var client = new HttpClient(handler);
        SteamOwnershipResult result = await new SteamOwnershipClient(client, SteamProofVerifierShould.Configuration(), new FakeTimeProvider(Now))
            .CheckAsync(StoreAuthenticationTestData.SteamAppId, SteamProofVerifierShould.SteamId, CancellationToken.None);
        Assert.Equal(SteamOwnershipStatus.Owned, result.Status);
        Assert.Equal(Now.UtcDateTime.AddMinutes(5), result.AuthorizedUntil);
    }

    [Theory]
    [InlineData("{\"appownership\":{\"ownsapp\":false,\"permanent\":true,\"timeexpires\":\"never\"}}", SteamOwnershipStatus.NotOwned)]
    [InlineData("{\"appownership\":{\"ownsapp\":true,\"timeexpires\":\"2026-10-04T11:59:00Z\"}}", SteamOwnershipStatus.NotOwned)]
    [InlineData("{}", SteamOwnershipStatus.ProviderUnavailable)]
    [InlineData("{\"appownership\":{\"permanent\":true}}", SteamOwnershipStatus.ProviderUnavailable)]
    [InlineData("{\"appownership\":{\"ownsapp\":\"true\"}}", SteamOwnershipStatus.ProviderUnavailable)]
    [InlineData("{\"appownership\":{\"ownsapp\":true,\"timeexpires\":\"bad-expiry\"}}", SteamOwnershipStatus.ProviderUnavailable)]
    [InlineData("not-json", SteamOwnershipStatus.ProviderUnavailable)]
    public async Task Fail_closed_on_missing_malformed_or_expired_evidence(string body, SteamOwnershipStatus expected)
    {
        using var handler = new RecordingSteamHandler { Body = body };
        using var client = new HttpClient(handler);
        SteamOwnershipResult result = await new SteamOwnershipClient(client, SteamProofVerifierShould.Configuration(), new FakeTimeProvider(Now))
            .CheckAsync(StoreAuthenticationTestData.SteamAppId, SteamProofVerifierShould.SteamId, CancellationToken.None);
        Assert.Equal(expected, result.Status);
        Assert.True(result.AuthorizedUntil <= Now.UtcDateTime);
    }

    [Fact]
    public async Task Distinguish_provider_outage_from_an_explicit_denial()
    {
        using var handler = new RecordingSteamHandler { Status = HttpStatusCode.TooManyRequests };
        using var client = new HttpClient(handler);
        SteamOwnershipResult result = await new SteamOwnershipClient(client, SteamProofVerifierShould.Configuration(), new FakeTimeProvider(Now))
            .CheckAsync(StoreAuthenticationTestData.SteamAppId, SteamProofVerifierShould.SteamId, CancellationToken.None);
        Assert.Equal(SteamOwnershipStatus.ProviderUnavailable, result.Status);
        Assert.Equal(2, handler.Requests.Count);
    }
}
