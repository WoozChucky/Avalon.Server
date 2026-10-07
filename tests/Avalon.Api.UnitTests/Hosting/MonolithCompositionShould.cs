using Avalon.Api.Commerce;
using Avalon.Api.Distribution;
using Avalon.Api.Hosting;
using Avalon.Api.Hosting.Middlewares;
using Avalon.Api.Identity.Authentication;
using Avalon.Api.Identity.Config;
using Avalon.Api.Identity.Exceptions;
using Avalon.Api.Testing;
using Avalon.Api.Worlds.Exceptions;
using Avalon.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Avalon.Api.UnitTests.Hosting;

/// <summary>
/// The process that runs every service, as a deployment with no <c>Application:Services</c> does, is the Avalon.Api of
/// before the split (#794): the services add to the shared hosting their exceptions' answers, the game servers'
/// rate-limit partition, the Steam callback's query kept out of the request log and the startup checks; it binds the
/// store settings once, owns the auth schema, and runs Program's middleware in Program's order. Built as the host
/// builds it, for every service it runs (<see cref="ApiServices.All"/>).
/// </summary>
public sealed class MonolithCompositionShould : IAsyncDisposable
{
    private readonly WebApplication _app;

    public MonolithCompositionShould()
    {
        WebApplicationBuilder builder = AvalonApiHost.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = Environments.Production }, ApiServices.All, configure: b =>
            {
                b.WebHost.UseTestServer();
                b.Logging.ClearProviders();
                b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["Application:Authentication:SigningKey"] = ApiTestHost.SigningKey,
                    ["Application:Authentication:SigningKeyId"] = ApiTestHost.SigningKeyId,
                    ["Application:GameAuth:HostKey"] = ApiTestHost.LegacySigningKey,
                    ["Database:Worlds:1:World:ConnectionString"] = "Host=w1",
                    ["Database:Worlds:1:Characters:ConnectionString"] = "Host=c1",
                    // Identity's store settings, with a playtest that admits world 3 alone.
                    ["Application:StoreAuthentication:SteamAppId"] = StoreAuthenticationTestData.SteamAppId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["Application:StoreAuthentication:SteamPublisherKey"] = "test-only",
                    ["Application:StoreAuthentication:SteamPlaytest:Enabled"] = "true",
                    ["Application:StoreAuthentication:SteamPlaytest:AppId"] = "2514590",
                    ["Application:StoreAuthentication:SteamPlaytest:AllowedWorldIds:0"] = "3",
                });
            });
        _app = builder.Build();
    }

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [Fact]
    public void Answer_the_services_exceptions_through_their_mappers()
    {
        Type[] mappers = _app.Services.GetServices<IExceptionProblemMapper>().Select(mapper => mapper.GetType()).ToArray();

        Assert.Equal(
            [typeof(IdentityProblemMapper), typeof(WorldsProblemMapper), typeof(CommerceProblemMapper), typeof(DistributionProblemMapper)],
            mappers);
    }

    [Fact]
    public void Count_game_servers_in_their_own_rate_limit_partition()
    {
        Assert.IsType<GameServerRateLimitWorkloads>(Assert.Single(_app.Services.GetServices<IRateLimitWorkloads>()));
    }

    [Fact]
    public void Keep_the_steam_callbacks_query_out_of_the_request_log()
    {
        RequestLoggingOptions options = _app.Services.GetRequiredService<IOptions<RequestLoggingOptions>>().Value;

        Assert.True(options.HidesQueryOf(SteamWebLinkOptions.CallbackPath));
        Assert.False(options.HidesQueryOf("/account"));
    }

    [Fact]
    public void Check_the_store_and_the_steam_web_link_before_serving()
    {
        Assert.IsType<IdentityStartupCheck>(Assert.Single(_app.Services.GetServices<IApiStartupCheck>()));
    }

    /// <summary>
    /// Identity and commerce both read Application:StoreAuthentication, and the process binds it once: a second bind
    /// would append each array entry again, and the playtest's doubled worlds would be refused as duplicates.
    /// </summary>
    [Fact]
    public void Bind_the_store_authentication_section_once()
    {
        StoreAuthenticationConfiguration store = _app.Services.GetRequiredService<IOptions<StoreAuthenticationConfiguration>>().Value;

        Assert.Equal(new ushort[] { 3 }, store.SteamPlaytest.AllowedWorldIds);
    }

    [Fact]
    public void Own_the_auth_schema()
    {
        Assert.Equal(AuthSchemaRole.Owner, _app.Services.GetRequiredService<AuthSchemaGate>().Role);
    }

    /// <summary>
    /// Identity's Steam OpenID callback runs before authentication and its game workload authentication after it, at
    /// the hooks of the one pipeline, behind the game workload routes' port check (design D7.4); the world routes are
    /// the worlds service's.
    /// </summary>
    [Fact]
    public async Task Run_the_middleware_of_Program_in_its_order()
    {
        List<string> names = await MiddlewareRecorder.RecordAsync(Environments.Production, ApiServices.All);

        Assert.Equal(
        [
            "ExceptionHandlerMiddleware",
            "RequestLoggingMiddleware",
            "ForwardedHeadersSetup",
            "ForwardedHeadersMiddleware",
            "EndpointRoutingMiddleware",
            "CorsMiddleware",
            "GameInternalRoutes",
            "SteamOpenIdCallbackMiddleware",
            "AuthenticationMiddleware",
            "GameWorkloadAuthentication",
            "ApiRateLimiting",
            "RateLimitingMiddleware",
            "WorldRouteMiddleware",
            "AuthorizationMiddlewareInternal",
        ], names);
    }
}
