using Avalon.Infrastructure.Configuration;
using Avalon.Infrastructure.Login;
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

    /// <summary>
    /// The login policy both servers share (#478). The host registers its <see cref="ILoginLimits"/>
    /// (the Auth server's <c>Application</c> settings, the API's <c>Application:Authentication</c>).
    /// </summary>
    public static IServiceCollection AddLoginPolicy(this IServiceCollection services)
    {
        services.AddSingleton<IPasswordVerifier, BCryptPasswordVerifier>();
        services.AddScoped<PasswordLoginPolicy>();
        services.AddScoped<MfaLoginPolicy>();
        return services;
    }

    public static IServiceCollection AddSecureRandom(this IServiceCollection services)
    {
        services.AddSingleton<ISecureRandom, SecureRandom>();
        return services;
    }

}
