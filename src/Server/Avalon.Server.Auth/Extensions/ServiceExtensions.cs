using Avalon.Database;
using Avalon.Database.Auth.Extensions;
using Avalon.Database.Extensions;
using Avalon.Infrastructure.Extensions;
using Avalon.Server.Auth.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Avalon.Infrastructure.Login;

namespace Avalon.Server.Auth.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddAuthServices(this IServiceCollection services)
    {
        services.AddOptions<HostingSecurity>()
            .BindConfiguration("Hosting:Security")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<AuthConfiguration>()
            .BindConfiguration("Application")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // The clock the online liveness sweep is due by (#555).
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<ILoginLimits>(sp => sp.GetRequiredService<IOptions<AuthConfiguration>>().Value);

        services.AddAuthDatabase()
            .ValidateDatabasesOnStart(DatabaseConnections.Auth)
            .AddLoginPolicy()
            .AddCache()
            .AddMfaService()
            .AddSecureRandom();

        return services;
    }
}
