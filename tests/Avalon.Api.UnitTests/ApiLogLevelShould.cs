using Avalon.Api.Hosting.Config;
using Avalon.Hosting.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Avalon.Api.UnitTests;

/// <summary>
/// The API writes nothing at Debug in Production (#1051). <c>AddCustomLogging</c> builds Serilog at a Debug minimum and
/// <c>AddSerilog</c> lets every level through to it, so <c>Logging:LogLevel</c> does not filter Serilog: only
/// <c>Serilog:MinimumLevel</c> does. Without its <c>Default</c> in the shipped <c>appsettings.json</c>, production
/// identity wrote three Debug lines for every game-server request (each lease heartbeat), under categories no override
/// named: the workload scheme's handler logs under its own type, the authorization line under
/// <c>DefaultAuthorizationService</c>.
/// </summary>
public sealed class ApiLogLevelShould
{
    [Theory]
    [InlineData("Avalon.Api.Identity.Authentication.GameServerAuthHandler")]
    [InlineData("Microsoft.AspNetCore.Authorization.DefaultAuthorizationService")]
    [InlineData("Avalon.Api.Hosting.Middlewares.RequestLoggingMiddleware")]
    public void Write_nothing_at_Debug_in_Production(string category)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = RepositoryRoot.PathOf("src/Server/Avalon.Api"),
            EnvironmentName = Environments.Production,
        });
        var services = new ServiceCollection();
        services.AddCustomLogging(ApiConfiguration.Sources(builder));
        using ServiceProvider provider = services.BuildServiceProvider();

        ILogger logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(category);

        Assert.False(logger.IsEnabled(LogLevel.Debug));
        Assert.True(logger.IsEnabled(LogLevel.Information));
    }
}
