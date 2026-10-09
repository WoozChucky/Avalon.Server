using Avalon.Api.Hosting.Config;
using Avalon.Api.Hosting.Middlewares;
using Avalon.Api.Hosting.Worlds;
using Avalon.Api.Identity;
using Avalon.Api.Identity.Config;
using Avalon.Api.Testing;
using Avalon.Api.Worlds;
using Avalon.Domain.Auth;
using Avalon.Hosting;
using Avalon.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Avalon.Api.Hosting.UnitTests.Hosting;

/// <summary>
/// #543: the api's cache binding, <c>Application:Cache</c>, is validated at startup, so a missing
/// host fails there, naming the setting, rather than on the first request that reaches Redis.
/// Likewise Database:Auth and, in <see cref="ApiStartup"/>, Database:Worlds (#523).
/// The api's own registrations are composed as the host registers them: the shared hosting for the services' needs,
/// then identity's and worlds'.
/// </summary>
public class ApiStartupValidationShould
{
    /// <summary>
    /// Identity refuses to start without a secret it needs, naming its setting, before any database call: the store's
    /// publisher key, and the game-auth host key, which nothing stands in for since #801.
    /// </summary>
    [Theory]
    [InlineData("Application:StoreAuthentication:SteamPublisherKey", typeof(OptionsValidationException), "Application:StoreAuthentication")]
    [InlineData("Application:GameAuth:HostKey", typeof(InvalidOperationException), "Application:GameAuth:HostKey is not set")]
    public async Task Refuse_a_missing_identity_secret_before_any_database_call(string setting, Type refusal, string named)
    {
        await using ServiceProvider provider = Build("localhost:6379", (setting, null));
        Exception refused = await Assert.ThrowsAsync(refusal,
            () => ApiStartup.ValidateAndMigrateAsync(provider, NullLogger.Instance));
        Assert.Contains(named, refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("private-test-publisher-key", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    public async Task Refuse_missing_or_zero_Steam_app_id_before_any_database_call(string? appId)
    {
        await using ServiceProvider provider = Build("localhost:6379", ("Application:StoreAuthentication:SteamAppId", appId));
        OptionsValidationException refused = await Assert.ThrowsAsync<OptionsValidationException>(
            () => ApiStartup.ValidateAndMigrateAsync(provider, NullLogger.Instance));
        Assert.Contains("Application:StoreAuthentication:SteamAppId", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("private-test-publisher-key", refused.Message, StringComparison.Ordinal);
    }

    private const string Unreachable = "Host=127.0.0.1;Port=1;Timeout=1;Database=none;Username=none;Password=none";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Refuse_to_start_without_a_cache_host(string? host)
    {
        using ServiceProvider provider = Build(host);

        OptionsValidationException refused = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains("'CacheConfiguration' members: 'Host'", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>#523: Database:Worlds is refused at startup, naming the setting, before any database call.</summary>
    [Fact]
    public async Task Refuse_to_start_without_a_world_before_any_database_call()
    {
        await using ServiceProvider provider = Build("localhost:6379",
            ("Database:Worlds:1:World:ConnectionString", null), ("Database:Worlds:1:Characters:ConnectionString", null));

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ApiStartup.ValidateAndMigrateAsync(provider, NullLogger.Instance));

        Assert.Contains("Database:Worlds lists no world", refused.Message, StringComparison.Ordinal);
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

        OptionsValidationException refused = await Assert.ThrowsAsync<OptionsValidationException>(
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

    /// <summary>
    /// The load-test settings: the sources exempt from the per-source limits are parsed before the api serves, so an
    /// entry that is not an address or a network refuses startup, naming it (an IPv4 address with fewer than four parts
    /// would otherwise parse as another address); and a load-test account cap below one refuses too.
    /// </summary>
    [Theory]
    [InlineData("Application:RateLimiting:ExemptSources", "10.0.0.5|10.1.0.0/16|::1", null)]
    [InlineData("Application:RateLimiting:ExemptSources", "10.0.0.5|not-an-ip", "\"not-an-ip\"")]
    [InlineData("Application:RateLimiting:ExemptSources", "10.1", "\"10.1\"")]
    [InlineData("Application:LoadTest:MaxAccounts", "0", "Application:LoadTest:MaxAccounts must be at least 1")]
    public void Validate_the_load_test_settings_at_startup(string setting, string values, string? refusalNaming)
    {
        using ServiceProvider provider = Build("localhost:6379",
            values.Split('|').Select((value, i) => (setting.EndsWith("ExemptSources", StringComparison.Ordinal)
                ? $"{setting}:{i}" : setting, (string?)value)).ToArray());

        if (refusalNaming is null)
        {
            provider.GetRequiredService<IStartupValidator>().Validate();
            Assert.True(provider.GetRequiredService<IExemptSources>().IsExempt(System.Net.IPAddress.Parse("10.1.2.3")));
            return;
        }

        OptionsValidationException refused = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());
        Assert.Contains(refusalNaming, refused.Message, StringComparison.Ordinal);
    }

    /// <summary>A reload timeout of zero or less would time every save out before it started waiting.</summary>
    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-00:00:05")]
    public void Refuse_to_start_with_a_non_positive_template_reload_timeout(string timeout)
    {
        using ServiceProvider provider = Build("localhost:6379", ("Application:Templates:ReloadTimeout", timeout));

        OptionsValidationException refused = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains("Application:Templates:ReloadTimeout must be a positive time span", refused.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Refuse_to_start_without_a_template_reload_timeout()
    {
        using ServiceProvider provider = Build("localhost:6379", ("Application:Templates:ReloadTimeout", null));

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());
    }

    [Fact]
    public void Refuse_to_start_without_the_auth_database()
    {
        using ServiceProvider provider = Build("localhost:6379", ("Database:Auth:ConnectionString", null));

        OptionsValidationException refused = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains("Database:Auth:ConnectionString is required.", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// #523: <see cref="ApiStartup"/> checks each world's databases tolerantly, so a world whose
    /// check fails is marked unavailable and startup goes on, while the other world stays available.
    /// </summary>
    [Fact]
    public async Task Start_when_one_worlds_check_fails_and_mark_only_that_world_unavailable()
    {
        await using ServiceProvider provider = Build("localhost:6379",
            services => services.AddSingleton(sp => new ApiDatabaseMigrator(
                sp.GetRequiredService<ILogger<ApiDatabaseMigrator>>(),
                migrate: (_, _) => Task.CompletedTask,
                canConnect: (context, _) => (context.Database.GetConnectionString() ?? "").Contains("Database=world2", StringComparison.Ordinal)
                    ? throw new InvalidOperationException("world 2 is down")
                    : Task.FromResult(true))),
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
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Database:Auth:ConnectionString"] = Unreachable,
            [GameAuthConfig.HostKeySetting] = ApiTestHost.HostKey,
            ["Application:StoreAuthentication:SteamPublisherKey"] = "private-test-publisher-key",
            ["Application:StoreAuthentication:SteamAppId"] = StoreAuthenticationTestData.SteamAppId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Application:SteamWebLink:CallbackUrl"] = "https://api.example.test/account/links/steam/callback",
            ["Application:SteamWebLink:SiteUrl"] = "https://web.example.test",
            // appsettings.json's default; the options check refuses a host without one.
            ["Application:Templates:ReloadTimeout"] = "00:00:10",
            ["Database:Worlds:1:World:ConnectionString"] = Unreachable,
            ["Database:Worlds:1:Characters:ConnectionString"] = Unreachable,
        };
        // An override with a null value removes that key.
        foreach ((string key, string? value) in overrides)
            if (value is null) settings.Remove(key); else settings[key] = value;
        if (cacheHost is not null)
            settings["Application:Cache:Host"] = cacheHost;

        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        ApplicationConfig config = new()
        {
            Environment = new EnvironmentConfig(),
            Authentication = ApiTestHost.AuthConfig,
            // Identity's own section, bound from the settings as the host binds it.
            GameAuth = configuration.GetSection("Application:GameAuth").Get<GameAuthConfig>(),
            Notification = new NotificationConfig(),
            Cache = new CacheConfiguration(),
        };

        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddHttpContextAccessor();
        services.AddSingleton(config);
        services.AddSingleton(config.Environment);
        services.AddSingleton(config.Authentication);
        services.AddSingleton(config.Notification);
        services.AddSingleton(config.Cache);
        services.AddSingleton(ApiTestHost.Keys);
        // The shared hosting for the needs of the api's services, as the host registers it before them, then the
        // services' own registrations (#794).
        services.AddApiHosting(ApiServiceNeeds.Union(ApiServices.All.Select(service => service.Needs)), config.ForwardedHeaders);
        services.AddIdentity(config);
        services.AddWorlds(configuration);
        configure?.Invoke(services);

        return services.BuildServiceProvider(AvalonServiceProvider.Options);
    }
}
