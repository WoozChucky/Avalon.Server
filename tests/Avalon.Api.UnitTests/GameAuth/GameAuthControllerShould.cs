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
    public void Expose_only_common_attempt_and_store_proof_contracts()
    {
        var routes = typeof(GameAuthController).GetMethods().SelectMany(method =>
            method.GetCustomAttributes<HttpPostAttribute>().Select(route => route.Template)).ToArray();
        Assert.Contains("provider-attempts", routes);
        Assert.Contains("store/proof", routes);
        Assert.DoesNotContain("attempts", routes);
        Assert.DoesNotContain("store/steam", routes);
        var assembly = typeof(GameProviderAttemptRequest).Assembly;
        Assert.Null(assembly.GetType("Avalon.Api.Contract.GameAttemptRequest"));
        Assert.Null(assembly.GetType("Avalon.Api.Contract.SteamGameProofRequest"));
    }

    [Theory]
    [InlineData("https", "steam.main", true)]
    [InlineData("https", "avalon.base", true)]
    [InlineData("http", "steam.main", false)]
    [InlineData("https", "unregistered.main", false)]
    public async Task Generic_attempt_transport_resolves_only_registered_configured_authority(string scheme, string application, bool accepted)
    {
        var store = new AtomicAuthStore(); var crypto = new GameAuthCryptography(new byte[32]);
        var config = Options.Create(new StoreAuthenticationConfiguration { SteamAppId = 2499460 });
        var service = TestGameAuthorization.Create(store, new(store, crypto, config, TimeProvider.System), crypto,
            Substitute.For<IAccountRepository>(), Substitute.For<IRefreshTokenRepository>(), Substitute.For<IExternalIdentityRepository>(),
            Substitute.For<ILicenseObservationRepository>(), Substitute.For<ISteamProofVerifier>(), Substitute.For<ISteamOwnershipClient>(), config, TimeProvider.System);
        var controller = new GameAuthController(service) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        controller.Request.Scheme = scheme;
        var result = await controller.ProviderAttempt(new GameProviderAttemptRequest { ApplicationKey = application, ProtocolVersion = "0.2.0",
            ClientRunId = Guid.NewGuid(), LinkChallenge = new string('A', 43) }, default);
        if (accepted)
        {
            var reply = Assert.IsType<ProviderAuthAttemptReply>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.InRange(reply.ExpectedChallenge.Length, 1, 128);
        }
        else { Assert.IsType<BadRequestObjectResult>(result); Assert.Empty(store.Entries); }
    }
    [Fact]
    public void Generic_proof_contract_carries_no_client_selected_product_or_account_authority()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GameProviderProofRequest>("{\"Provider\":\"test-store\",\"Proof\":\"opaque\",\"AccountId\":7}"));
        Assert.DoesNotContain("private-proof", new GameProviderProofRequest { Provider = "test-store", AttemptCredential = "private-attempt", Proof = "private-proof" }.ToString());
    }
    [Theory]
    [InlineData("steam.main", true)]
    [InlineData("avalon.base", true)]
    [InlineData("steam.playtest", false)]
    [InlineData("", false)]
    public async Task Application_selection_is_explicit_and_disabled_applications_are_refused(string application, bool accepted)
    {
        var store = new AtomicAuthStore();
        var crypto = new GameAuthCryptography(new byte[32]);
        var config = Options.Create(new StoreAuthenticationConfiguration { SteamAppId = 480, SteamPublisherKey = "test-secret" });
        var service = TestGameAuthorization.Create(store, new AuthAttemptStore(store, crypto, config, TimeProvider.System), crypto,
            Substitute.For<IAccountRepository>(), Substitute.For<IRefreshTokenRepository>(), Substitute.For<IExternalIdentityRepository>(),
            Substitute.For<ILicenseObservationRepository>(), Substitute.For<ISteamProofVerifier>(), Substitute.For<ISteamOwnershipClient>(), config, TimeProvider.System);
        var controller = new GameAuthController(service) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        controller.Request.Scheme = "https";
        var result = await controller.ProviderAttempt(new GameProviderAttemptRequest { ApplicationKey = application, ProtocolVersion = "0.2.0", ClientRunId = Guid.NewGuid(), LinkChallenge = new string('A', 43) }, default);
        if (accepted) Assert.IsType<OkObjectResult>(result);
        else { Assert.IsType<BadRequestObjectResult>(result); Assert.Empty(store.Entries); }
    }

    [Fact]
    public async Task Refuse_plaintext_before_creating_an_attempt()
    {
        var store = new AtomicAuthStore();
        var crypto = new GameAuthCryptography(new byte[32]);
        var config = Options.Create(new StoreAuthenticationConfiguration { SteamAppId = StoreAuthenticationTestData.SteamAppId, SteamPublisherKey = "test-secret" });
        var service = TestGameAuthorization.Create(store, new AuthAttemptStore(store, crypto, config, TimeProvider.System), crypto,
            Substitute.For<IAccountRepository>(), Substitute.For<IRefreshTokenRepository>(), Substitute.For<IExternalIdentityRepository>(),
            Substitute.For<ILicenseObservationRepository>(), Substitute.For<ISteamProofVerifier>(), Substitute.For<ISteamOwnershipClient>(),
            config, TimeProvider.System);
        var controller = new GameAuthController(service) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        var result = await controller.ProviderAttempt(new GameProviderAttemptRequest
        {
            ApplicationKey = "steam.main", ProtocolVersion = "1", ClientRunId = Guid.NewGuid(), LinkChallenge = new string('A', 43),
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
        var service = TestGameAuthorization.Create(store, new AuthAttemptStore(store, crypto, config, TimeProvider.System), crypto,
            Substitute.For<IAccountRepository>(), Substitute.For<IRefreshTokenRepository>(), Substitute.For<IExternalIdentityRepository>(),
            Substitute.For<ILicenseObservationRepository>(), Substitute.For<ISteamProofVerifier>(), Substitute.For<ISteamOwnershipClient>(),
            config, TimeProvider.System);
        var controller = new GameAuthController(service) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        controller.Request.Scheme = "https";
        var result = Assert.IsType<BadRequestObjectResult>(await controller.ProviderAttempt(new GameProviderAttemptRequest
        { ApplicationKey = "steam.main", ProtocolVersion = version, ClientRunId = Guid.NewGuid(), LinkChallenge = new string('A',43) }, CancellationToken.None));
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
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GameProviderProofRequest>(body));
    }

    [Fact]
    public void Keep_credentials_out_of_response_debug_strings()
    {
        var reply = new GameAuthReply { State = "authorized", GameContextCredential = "private-context", GameContextRefreshToken = "private-refresh" };
        Assert.DoesNotContain("private-context", reply.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("private-refresh", reply.ToString(), StringComparison.Ordinal);
    }
}
