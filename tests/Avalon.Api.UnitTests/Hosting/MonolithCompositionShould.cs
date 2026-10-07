using Avalon.Api.Authentication;
using Avalon.Api.Commerce;
using Avalon.Api.Config;
using Avalon.Api.Distribution;
using Avalon.Api.Exceptions;
using Avalon.Api.Hosting;
using Avalon.Api.Hosting.Middlewares;
using Avalon.Api.Services;
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
/// The monolith adds to the shared hosting what Avalon.Api had built into it before the split (#794): its exceptions'
/// answers, its game servers' rate-limit partition, the Steam callback's query kept out of the request log, its
/// startup checks, and observability's per-world layout inputs. Built as the host builds it.
/// </summary>
public sealed class MonolithCompositionShould : IAsyncDisposable
{
    private readonly WebApplication _app;

    public MonolithCompositionShould()
    {
        WebApplicationBuilder builder = AvalonApiHost.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = Environments.Production }, [MonolithApi.Service], configure: b =>
            {
                b.WebHost.UseTestServer();
                b.Logging.ClearProviders();
                b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["Application:Authentication:IssuerSigningKey"] = new string('k', 64),
                    ["Database:Worlds:1:World:ConnectionString"] = "Host=w1",
                    ["Database:Worlds:1:Characters:ConnectionString"] = "Host=c1",
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
            [typeof(CommerceProblemMapper), typeof(IdentityProblemMapper), typeof(WorldsProblemMapper), typeof(DistributionProblemMapper)],
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

    [Fact]
    public void Own_the_auth_schema()
    {
        Assert.Equal(AuthSchemaRole.Owner, _app.Services.GetRequiredService<AuthSchemaGate>().Role);
    }
}
