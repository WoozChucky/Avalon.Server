using Avalon.Api.Authentication;
using Avalon.Api.Commerce;
using Avalon.Api.Config;
using Avalon.Api.Distribution;
using Avalon.Api.Exceptions;
using Avalon.Api.Hosting;
using Avalon.Api.Hosting.Middlewares;
using Avalon.Api.Services;
using Avalon.Api.Testing;
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
/// The API's services add to the shared hosting what Avalon.Api had built into it before the split (#794): their
/// exceptions' answers, the game servers' rate-limit partition, the Steam callback's query kept out of the request log,
/// the startup checks, and observability's per-world layout inputs. Built as the host builds it, for every service it
/// runs (<see cref="ApiServices.All"/>).
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
                    ["Application:Authentication:IssuerSigningKey"] = new string('k', 64),
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

    [Fact]
    public void Give_observability_each_worlds_layout_inputs()
    {
        Assert.IsType<WorldContentRepositories>(_app.Services.GetRequiredService<IWorldContentRepositories>());
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
}
