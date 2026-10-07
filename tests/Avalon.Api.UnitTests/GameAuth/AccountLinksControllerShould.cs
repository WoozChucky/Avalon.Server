using System.Reflection;
using System.Text.Json;
using Avalon.Api.Authentication;
using Avalon.Api.Contract;
using Avalon.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Avalon.Api.UnitTests.GameAuth;

public class AccountLinksControllerShould
{
    [Fact]
    public void Require_player_browser_authority_and_bounded_uncached_requests()
    {
        var type = typeof(AccountLinksController);
        Assert.Equal(AvalonRoles.Player, type.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Null(type.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Equal("account/links", type.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.True(type.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore);
        Assert.True(((IRequestSizeLimitMetadata)type.GetCustomAttribute<RequestSizeLimitAttribute>()!).MaxRequestBodySize <= 16384);
    }

    [Fact]
    public void Reject_client_account_authority_or_provider_identity_fields()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AccountLinkConfirmRequest>("{\"PendingLinkId\":\"11111111-1111-1111-1111-111111111111\",\"CurrentPassword\":\"test\",\"Confirmed\":true,\"AccountId\":\"7\"}"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GameLinkCompleteRequest>("{\"GameContextCredential\":\"test\",\"ConsentCode\":\"test\",\"PkceVerifier\":\"test\",\"Accepted\":true,\"SteamId\":\"76561198000000001\"}"));
    }
}
