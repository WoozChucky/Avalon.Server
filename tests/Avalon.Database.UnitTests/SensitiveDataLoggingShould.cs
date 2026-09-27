using Avalon.Configuration;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Extensions;
using Avalon.Database.Character;
using Avalon.Database.Character.Extensions;
using Avalon.Database.World;
using Avalon.Database.World.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Avalon.Database.UnitTests;

/// <summary>
/// #558: sensitive data logging puts every command's parameter values in the logs (password
/// verifiers, token hashes, MFA secrets on the auth database). It is on only when the host
/// environment is Development, for every context, however the context is built.
/// </summary>
public class SensitiveDataLoggingShould
{
    private const string NoServer = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none";

    public static TheoryData<string> Contexts => new() { "Auth", "Characters", "World" };

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Be_off_in_production(string context) =>
        Assert.False(SensitiveLogging(context, Environments.Production));

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Be_off_in_staging(string context) =>
        Assert.False(SensitiveLogging(context, Environments.Staging));

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Be_off_when_the_container_has_no_host_environment(string context) =>
        Assert.False(SensitiveLogging(context, environment: null));

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Stay_off_outside_development_even_when_configuration_asks_for_it(string context) =>
        Assert.False(SensitiveLogging(context, Environments.Production,
            ("Database:EnableSensitiveDataLogging", "true")));

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Be_on_in_development(string context) =>
        Assert.True(SensitiveLogging(context, Environments.Development));

    /// <summary>
    /// A named configuration (one per world, say) is held to the same rule as the default one:
    /// whatever it was configured with, only Development leaves it on.
    /// </summary>
    [Theory]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    [InlineData("Development", true)]
    public void Hold_a_named_configuration_to_the_environment(string environment, bool expected)
    {
        ServiceCollection services = new();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        var host = Substitute.For<IHostEnvironment>();
        host.EnvironmentName.Returns(environment);
        services.AddSingleton(host);
        services.AddAuthDatabase();
        services.Configure<DatabaseConfiguration>("world-1", c => c.EnableSensitiveDataLogging = true);

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Equal(expected,
            provider.GetRequiredService<IOptionsMonitor<DatabaseConfiguration>>().Get("world-1").EnableSensitiveDataLogging);
    }

    /// <summary>What the design-time factories hand the constructor: a configuration with only connection strings.</summary>
    [Fact]
    public void Be_off_for_a_context_built_from_a_default_configuration()
    {
        IOptions<DatabaseConfiguration> options = Options.Create(new DatabaseConfiguration
        {
            Auth = new DatabaseConnection { ConnectionString = NoServer },
            Characters = new DatabaseConnection { ConnectionString = NoServer },
            World = new DatabaseConnection { ConnectionString = NoServer },
        });

        using var auth = new AuthDbContext(NullLoggerFactory.Instance, options);
        using var characters = new CharacterDbContext(NullLoggerFactory.Instance, options);
        using var world = new WorldDbContext(NullLoggerFactory.Instance, options);

        Assert.False(IsEnabled(auth));
        Assert.False(IsEnabled(characters));
        Assert.False(IsEnabled(world));
    }

    private static bool SensitiveLogging(string context, string? environment,
        params (string Key, string Value)[] extra)
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Database:Auth:ConnectionString"] = NoServer,
            ["Database:Characters:ConnectionString"] = NoServer,
            ["Database:World:ConnectionString"] = NoServer,
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

        services.AddAuthDatabase();
        services.AddCharacterDatabase();
        services.AddWorldDatabase();

        using ServiceProvider provider = services.BuildServiceProvider();
        using DbContext db = context switch
        {
            "Auth" => provider.GetRequiredService<IDbContextFactory<AuthDbContext>>().CreateDbContext(),
            "Characters" => provider.GetRequiredService<IDbContextFactory<CharacterDbContext>>().CreateDbContext(),
            "World" => provider.GetRequiredService<IDbContextFactory<WorldDbContext>>().CreateDbContext(),
            _ => throw new ArgumentOutOfRangeException(nameof(context), context, null),
        };
        return IsEnabled(db);
    }

    private static bool IsEnabled(DbContext db) =>
        db.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.IsSensitiveDataLoggingEnabled
        ?? false;
}
