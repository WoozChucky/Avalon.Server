using Avalon.Database.Auth.Extensions;
using Avalon.Infrastructure.Extensions;
using Avalon.Server.Auth.Configuration;
using Avalon.Server.Auth.Services;

namespace Avalon.Server.Auth.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddAuthServices(this IServiceCollection services)
    {
        services.AddOptions<HostingSecurity>()
            .BindConfiguration("Hosting:Security");

        services.AddOptions<AuthConfiguration>()
            .BindConfiguration("Application")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IPasswordVerifier, BCryptPasswordVerifier>();

        services.AddAuthDatabase()
            .AddCache()
            .AddMfaService()
            .AddSecureRandom();

        return services;
    }
}
