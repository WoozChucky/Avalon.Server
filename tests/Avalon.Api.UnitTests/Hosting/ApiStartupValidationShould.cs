using Avalon.Api;
using Avalon.Api.Authentication.Jwt;
using Avalon.Api.Config;
using Avalon.Hosting;
using Avalon.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Avalon.Api.UnitTests.Hosting;

/// <summary>
/// #543: the api's cache binding, <c>Application:Cache</c>, is validated at startup, so a missing
/// host fails there, naming the setting, rather than on the first request that reaches Redis.
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

    private static ServiceProvider Build(string? cacheHost)
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
            ["Database:Characters:ConnectionString"] = Unreachable,
            ["Database:World:ConnectionString"] = Unreachable,
        };
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

        return services.BuildServiceProvider(AvalonServiceProvider.Options);
    }
}
