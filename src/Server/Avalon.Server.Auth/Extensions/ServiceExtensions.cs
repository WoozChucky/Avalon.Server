using Avalon.Database.Auth.Extensions;
using Avalon.Infrastructure.Extensions;
using Avalon.Server.Auth.Configuration;
using Microsoft.Extensions.Options;
using Avalon.Infrastructure.Login;

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

        services.AddSingleton<ILoginLimits>(sp => sp.GetRequiredService<IOptions<AuthConfiguration>>().Value);

        services.AddAuthDatabase()
            .AddLoginPolicy()
            .AddCache()
            .AddMfaService()
            .AddSecureRandom();

        return services;
    }
}
