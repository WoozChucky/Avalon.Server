using Avalon.Api;
using Avalon.Api.Authentication.Jwt;
using Avalon.Api.Config;
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
}
