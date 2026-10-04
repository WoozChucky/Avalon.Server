using System.Reflection;
using System.Text.Json;
using Avalon.Api.Contract;
using Avalon.Api.Controllers;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Infrastructure.GameAuth;
using Avalon.Infrastructure.StoreAuth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.GameAuth;

public class GameAuthControllerShould
{
    [Fact]
    public async Task Refuse_plaintext_before_creating_an_attempt()
    {
        var store = new AtomicAuthStore();
        var crypto = new GameAuthCryptography(new byte[32]);
        var config = Options.Create(new StoreAuthenticationConfiguration { SteamAppId = StoreAuthenticationTestData.SteamAppId, SteamPublisherKey = "test-secret" });
        var service = new GameAuthorizationService(store, new AuthAttemptStore(store, crypto, config, TimeProvider.System), crypto,
            Substitute.For<IAccountRepository>(), Substitute.For<IRefreshTokenRepository>(), Substitute.For<IExternalIdentityRepository>(),
            Substitute.For<ILicenseObservationRepository>(), Substitute.For<ISteamProofVerifier>(), Substitute.For<ISteamOwnershipClient>(),
            config, TimeProvider.System);
        var controller = new GameAuthController(service) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        var result = await controller.Attempt(new GameAttemptRequest
        {
            ChannelHint = "steam", ProtocolVersion = "1", ClientRunId = Guid.NewGuid(), LinkChallenge = new string('A', 43),
        }, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(store.Entries);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("0.1.0")]
    [InlineData("99")]
    public async Task Refuse_unsupported_protocols_before_allocating_an_attempt(string version)
    {
        var store = new AtomicAuthStore();
        var crypto = new GameAuthCryptography(new byte[32]);
        var config = Options.Create(new StoreAuthenticationConfiguration { SteamAppId = StoreAuthenticationTestData.SteamAppId, SteamPublisherKey = "test-secret" });
        var service = new GameAuthorizationService(store, new AuthAttemptStore(store, crypto, config, TimeProvider.System), crypto,
            Substitute.For<IAccountRepository>(), Substitute.For<IRefreshTokenRepository>(), Substitute.For<IExternalIdentityRepository>(),
            Substitute.For<ILicenseObservationRepository>(), Substitute.For<ISteamProofVerifier>(), Substitute.For<ISteamOwnershipClient>(),
            config, TimeProvider.System);
        var controller = new GameAuthController(service) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        controller.Request.Scheme = "https";
        var result = Assert.IsType<BadRequestObjectResult>(await controller.Attempt(new GameAttemptRequest
        { ChannelHint = "steam", ProtocolVersion = version, ClientRunId = Guid.NewGuid(), LinkChallenge = new string('A',43) }, CancellationToken.None));
        Assert.Equal("UNSUPPORTED_PROTOCOL", Assert.IsType<GameAuthReply>(result.Value).Error);
        Assert.Empty(store.Entries);
    }

    [Fact]
    public void Apply_no_store_and_keep_unversioned_native_routes()
    {
        var type = typeof(GameAuthController);
        Assert.True(type.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore);
        Assert.Equal("client/auth", type.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal(16384, ((Microsoft.AspNetCore.Http.Metadata.IRequestSizeLimitMetadata)type.GetCustomAttribute<RequestSizeLimitAttribute>()!).MaxRequestBodySize);
    }

    [Fact]
    public void Reject_client_selected_product_application_and_identity_fields()
    {
        var body = "{\"AttemptCredential\":\"" + new string('A', 43) + "\",\"TicketHex\":\"ABCD\",\"AppId\":480,\"Product\":\"another\"}";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SteamGameProofRequest>(body));
    }

    [Fact]
    public void Keep_credentials_out_of_response_debug_strings()
    {
        var reply = new GameAuthReply { State = "authorized", GameContextCredential = "private-context", GameContextRefreshToken = "private-refresh" };
        Assert.DoesNotContain("private-context", reply.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("private-refresh", reply.ToString(), StringComparison.Ordinal);
    }
}
