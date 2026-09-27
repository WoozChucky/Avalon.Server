using Avalon.Api;
using Avalon.Api.Authentication.Jwt;
using Avalon.Api.Config;
using Avalon.Api.Worlds;
using Avalon.Domain.Auth;
using Avalon.Hosting;
using Avalon.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Avalon.Api.UnitTests.Hosting;

/// <summary>
/// #543: the api's cache binding, <c>Application:Cache</c>, is validated at startup, so a missing
/// host fails there, naming the setting, rather than on the first request that reaches Redis.
/// Likewise Database:Auth and, in <see cref="ApiStartup"/>, Database:Worlds (#523).
/// The api's own registrations are composed as in <see cref="ApiHostGraphShould"/>.
/// </summary>
public class ApiStartupValidationShould
{
    private const string Unreachable = "Host=127.0.0.1;Port=1;Timeout=1;Database=none;Username=none;Password=none";

    [Fact]
    public void Pass_startup_validation_with_a_cache_host()
    {
        using ServiceProvider provider = Build("localhost:6379");

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Refuse_to_start_without_a_cache_host(string? host)
    {
        using ServiceProvider provider = Build(host);

        var refused = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains("'CacheConfiguration' members: 'Host'", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The check comes first in <see cref="ApiStartup"/>, the startup work Program runs before it
    /// serves: every database here points at a port nothing listens on, so a check that came after
    /// the migrations would fail with a connection error instead.
    /// </summary>
    [Fact]
    public async Task Refuse_to_start_without_a_cache_host_before_any_database_call()
    {
        await using ServiceProvider provider = Build(null);

        var refused = await Assert.ThrowsAsync<OptionsValidationException>(
            () => ApiStartup.ValidateAndMigrateAsync(provider, NullLogger.Instance));

        Assert.Contains("'CacheConfiguration' members: 'Host'", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>#523: Database:Worlds is refused at startup, naming the setting, before any database call.</summary>
    [Fact]
    public async Task Refuse_to_start_without_a_world_before_any_database_call()
    {
        await using ServiceProvider provider = Build("localhost:6379",
            ("Database:Worlds:1:World:ConnectionString", null), ("Database:Worlds:1:Characters:ConnectionString", null));

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ApiStartup.ValidateAndMigrateAsync(provider, NullLogger.Instance));

        Assert.Contains("Database:Worlds lists no world", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refuse_to_start_with_half_a_world()
    {
        await using ServiceProvider provider = Build("localhost:6379", ("Database:Worlds:1:Characters:ConnectionString", null));

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ApiStartup.ValidateAndMigrateAsync(provider, NullLogger.Instance));

        Assert.Contains("Database:Worlds:1:Characters:ConnectionString is missing", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Port=1", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// OpenAPI generation starts the host, which runs the options checks, with no world configured,
    /// and skips <see cref="ApiStartup"/>. So Database:Worlds is checked by ApiStartup, never by an
    /// options validation (#523).
    /// </summary>
    [Fact]
    public void Pass_the_options_checks_without_a_world()
    {
        using ServiceProvider provider = Build("localhost:6379",
            ("Database:Worlds:1:World:ConnectionString", null), ("Database:Worlds:1:Characters:ConnectionString", null));

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    /// <summary>#561: a request limit below one would refuse every request.</summary>
    [Theory]
    [InlineData("AnonymousPermitsPerMinute", "0")]
    [InlineData("AuthenticatedPermitsPerMinute", "0")]
    [InlineData("AnonymousPermitsPerMinute", "-5")]
    public async Task Refuse_to_start_with_a_request_limit_below_one_naming_it_before_any_database_call(string setting,
        string value)
    {
        await using ServiceProvider provider = Build("localhost:6379", ($"Application:RateLimiting:{setting}", value));

        var refused = await Assert.ThrowsAsync<OptionsValidationException>(
            () => ApiStartup.ValidateAndMigrateAsync(provider, NullLogger.Instance));

        Assert.Contains($"Application:RateLimiting:{setting} must be at least 1", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Pass_startup_validation_with_request_limits_of_one()
    {
        using ServiceProvider provider = Build("localhost:6379",
            ("Application:RateLimiting:AnonymousPermitsPerMinute", "1"),
            ("Application:RateLimiting:AuthenticatedPermitsPerMinute", "1"));

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    [Fact]
    public void Refuse_to_start_without_the_auth_database()
    {
        using ServiceProvider provider = Build("localhost:6379", ("Database:Auth:ConnectionString", null));

        var refused = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains("Database:Auth:ConnectionString is required.", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// #523: <see cref="ApiStartup"/> migrates through the tolerant migrator, so a world whose
    /// migration fails is marked unavailable and startup goes on, while the other world stays available.
    /// </summary>
    [Fact]
    public async Task Start_when_one_worlds_migration_fails_and_mark_only_that_world_unavailable()
    {
        await using ServiceProvider provider = Build("localhost:6379",
            services => services.AddSingleton(sp => new ApiDatabaseMigrator(
                sp.GetRequiredService<ILogger<ApiDatabaseMigrator>>(),
                (context, _) => (context.Database.GetConnectionString() ?? "").Contains("Database=world2", StringComparison.Ordinal)
                    ? throw new InvalidOperationException("world 2 is down")
                    : Task.CompletedTask)),
            ("Database:Worlds:2:World:ConnectionString", "Host=127.0.0.1;Port=1;Timeout=1;Database=world2;Username=none;Password=none"),
            ("Database:Worlds:2:Characters:ConnectionString", Unreachable));

        await ApiStartup.ValidateAndMigrateAsync(provider, NullLogger.Instance);

        WorldDatabases worlds = provider.GetRequiredService<WorldDatabases>();
        Assert.True(worlds.IsAvailable(new WorldId(1)));
        Assert.False(worlds.IsAvailable(new WorldId(2)));
    }

    private static ServiceProvider Build(string? cacheHost, params (string Key, string? Value)[] overrides) =>
        Build(cacheHost, null, overrides);

    private static ServiceProvider Build(string? cacheHost, Action<IServiceCollection>? configure,
        params (string Key, string? Value)[] overrides)
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
            ["Database:Auth:ConnectionString"] = Unreachable,
            ["Database:Worlds:1:World:ConnectionString"] = Unreachable,
            ["Database:Worlds:1:Characters:ConnectionString"] = Unreachable,
        };
        // An override with a null value removes that key.
        foreach ((string key, string? value) in overrides)
            if (value is null) settings.Remove(key); else settings[key] = value;
        if (cacheHost is not null)
            settings["Application:Cache:Host"] = cacheHost;

        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddHttpContextAccessor();
        services.AddSingleton(config);
        services.AddSingleton(config.Environment);
        services.AddSingleton(config.Authentication);
        services.AddSingleton(config.Notification);
        services.AddSingleton(config.Cache);
        services.AddSingleton(JwtSigningKey.Create(config.Authentication));
        services.AddInfrastructure(config);
        configure?.Invoke(services);

        return services.BuildServiceProvider(AvalonServiceProvider.Options);
    }
}
