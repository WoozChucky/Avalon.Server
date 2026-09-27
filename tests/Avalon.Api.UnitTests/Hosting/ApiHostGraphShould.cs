using Avalon.Api;
using Avalon.Api.Authentication.Jwt;
using Avalon.Api.Config;
using Avalon.Api.Worlds;
using Avalon.Configuration;
using Avalon.Hosting;
using Avalon.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Avalon.Api.UnitTests.Hosting;

/// <summary>
/// The api's own registrations, composed as its entry point composes them, built under the
/// validation every Avalon host now enables. The api is the one host where scoped was the right
/// answer — its services are per request — so this pins that the move to a context factory left
/// that graph intact and that nothing in it captures.
/// </summary>
public class ApiHostGraphShould
{
    [Fact]
    public void Build_with_no_captured_scoped_services()
    {
        ApplicationConfig config = new()
        {
            Environment = new EnvironmentConfig(),
            Authentication = new AuthenticationConfig { IssuerSigningKey = new string('k', 64) },
            Notification = new NotificationConfig(),
            Cache = new CacheConfiguration(),
        };

        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddHttpContextAccessor();
        services.AddSingleton(config);
        services.AddSingleton(config.Environment);
        services.AddSingleton(config.Authentication);
        services.AddSingleton(config.Notification);
        services.AddSingleton(config.Cache);
        // AddAuth, which Program.cs calls first, registers the signing key JwtUtils takes (#482).
        services.AddSingleton(JwtSigningKey.Create(config.Authentication));
        services.AddInfrastructure(config);

        ServiceProvider provider = services.BuildServiceProvider(AvalonServiceProvider.Options);

        Assert.NotNull(provider);
    }

    /// <summary>
    /// The api's context factories are the per-request world ones, not a single database's (#523):
    /// a regression to AddWorldDatabase() would silently serve one world everywhere.
    /// </summary>
    [Fact]
    public void Open_the_requests_world_for_every_repository()
    {
        ApplicationConfig config = new()
        {
            Environment = new EnvironmentConfig(),
            Authentication = new AuthenticationConfig { IssuerSigningKey = new string('k', 64) },
            Notification = new NotificationConfig(),
            Cache = new CacheConfiguration(),
        };
        ServiceCollection services = new();
        services.AddLogging();
        // WorldDatabases is built from configuration when first resolved, as in the api.
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Database:Worlds:1:World:ConnectionString"] = "Host=w",
                ["Database:Worlds:1:Characters:ConnectionString"] = "Host=c",
            })
            .Build());
        services.AddHttpContextAccessor();
        services.AddSingleton(config);
        services.AddSingleton(config.Authentication);
        services.AddInfrastructure(config);

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<CurrentWorldDbContextFactory<Avalon.Database.World.WorldDbContext>>(
            provider.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<Avalon.Database.World.WorldDbContext>>());
        Assert.IsType<CurrentWorldDbContextFactory<Avalon.Database.Character.CharacterDbContext>>(
            provider.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<Avalon.Database.Character.CharacterDbContext>>());
    }
}
