using Avalon.Infrastructure.Configuration;
using Avalon.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Avalon.Infrastructure.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddCache(this IServiceCollection services)
    {
        services.AddOptions<CacheConfiguration>()
            .BindConfiguration("Cache")
            .ValidateDataAnnotations();
        services.AddSingleton<IReplicatedCache, ReplicatedCache>();
        return services;
    }

    public static IServiceCollection AddMfaService(this IServiceCollection services)
    {
        services.AddScoped<IMFAHashService, MFAHashService>();
        services.AddScoped<IMFAService, MFAService>();
        return services;
    }

    public static IServiceCollection AddSecureRandom(this IServiceCollection services)
    {
        services.AddSingleton<ISecureRandom, SecureRandom>();
        return services;
    }

}
