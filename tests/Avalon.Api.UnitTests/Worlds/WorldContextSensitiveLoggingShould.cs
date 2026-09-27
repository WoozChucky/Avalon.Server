using Avalon.Api.Authentication.Jwt;
using Avalon.Api.Config;
using Avalon.Api.Worlds;
using Avalon.Database.Character;
using Avalon.Database.World;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.Worlds;

/// <summary>
/// #558 for the api's per-world contexts (#523): each world's World and Characters context is built
/// by <see cref="ConfiguredWorldDbContextFactory"/>, not by the database registration, and follows
/// the same rule: sensitive data logging on only when the host environment is Development.
/// </summary>
public class WorldContextSensitiveLoggingShould
{
    private const string NoServer = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none";

    [Theory]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    [InlineData("Development", true)]
    public void Follow_the_host_environment_for_every_world(string environment, bool expected)
    {
        using ServiceProvider provider = Build(environment, ("Database:EnableSensitiveDataLogging", "true"));
        IWorldDbContextFactory contexts = provider.GetRequiredService<IWorldDbContextFactory>();

        foreach (ushort id in new ushort[] { 1, 2 })
        {
            using WorldDbContext world = contexts.CreateWorld(new WorldId(id));
            using CharacterDbContext characters = contexts.CreateCharacters(new WorldId(id));

            Assert.Equal(expected, IsEnabled(world));
            Assert.Equal(expected, IsEnabled(characters));
        }
    }

    [Fact]
    public void Be_off_when_the_container_has_no_host_environment()
    {
        using ServiceProvider provider = Build(environment: null);
        IWorldDbContextFactory contexts = provider.GetRequiredService<IWorldDbContextFactory>();

        using WorldDbContext world = contexts.CreateWorld(new WorldId(1));
        using CharacterDbContext characters = contexts.CreateCharacters(new WorldId(1));

        Assert.False(IsEnabled(world));
        Assert.False(IsEnabled(characters));
    }

    /// <summary>The api's own registrations, composed as in ApiHostGraphShould, with two worlds.</summary>
    private static ServiceProvider Build(string? environment, params (string Key, string Value)[] extra)
    {
        ApplicationConfig config = new()
        {
            Environment = new EnvironmentConfig(),
            Authentication = new AuthenticationConfig { IssuerSigningKey = new string('k', 64) },
            Notification = new NotificationConfig(),
            Cache = new CacheConfiguration(),
        };
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Database:Auth:ConnectionString"] = NoServer,
            ["Database:Worlds:1:World:ConnectionString"] = NoServer,
            ["Database:Worlds:1:Characters:ConnectionString"] = NoServer,
            ["Database:Worlds:2:World:ConnectionString"] = NoServer,
            ["Database:Worlds:2:Characters:ConnectionString"] = NoServer,
        };
        foreach ((string key, string value) in extra)
            settings[key] = value;

        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        if (environment is not null)
        {
            var host = Substitute.For<IHostEnvironment>();
            host.EnvironmentName.Returns(environment);
            services.AddSingleton(host);
        }

        services.AddHttpContextAccessor();
        services.AddSingleton(config);
        services.AddSingleton(config.Environment);
        services.AddSingleton(config.Authentication);
        services.AddSingleton(config.Notification);
        services.AddSingleton(config.Cache);
        services.AddSingleton(JwtSigningKey.Create(config.Authentication));
        services.AddInfrastructure(config);

        return services.BuildServiceProvider();
    }

    private static bool IsEnabled(DbContext db) =>
        db.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.IsSensitiveDataLoggingEnabled
        ?? false;
}
