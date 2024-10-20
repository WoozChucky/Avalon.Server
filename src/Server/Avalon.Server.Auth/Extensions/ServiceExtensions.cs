using Avalon.Database.Auth.Extensions;
using Avalon.Infrastructure.Extensions;
using Avalon.Server.Auth.Configuration;

namespace Avalon.Server.Auth.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddAuthServices(this IServiceCollection services)
    {
        services.AddOptions<HostingSecurity>()
            .BindConfiguration("Hosting:Security");

        services.AddAuthDatabase()
            .AddCache()
            .AddMfaService();

        return services;
    }
}
