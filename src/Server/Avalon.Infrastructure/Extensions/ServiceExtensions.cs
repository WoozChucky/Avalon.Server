using Avalon.Configuration;
using Avalon.Infrastructure.Configuration;
using Avalon.Infrastructure.Login;
using Avalon.Infrastructure.Services;
using Avalon.Infrastructure.WorldMaintenance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Avalon.Infrastructure.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddCache(this IServiceCollection services)
    {
        services.AddOptions<CacheConfiguration>()
            .BindConfiguration("Cache")
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IReplicatedCache, ReplicatedCache>();
        services.AddSingleton<IWorldReadiness, WorldReadiness>();
        return services;
    }

    public static IServiceCollection AddWorldMaintenanceControl(this IServiceCollection services)
    {
        services.AddSingleton<IWorldMaintenanceControl, WorldMaintenanceControl>();
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

    /// <summary>
    /// Binds <c>Application:StoreAuthentication</c> to <see cref="StoreAuthenticationConfiguration"/> once per service
    /// collection, however many API services ask (#794): identity and commerce both read it, and a process may run both.
    /// A second bind would append every array entry again (a playtest's or an additional application's allowed
    /// worlds), and the section's own rules refuse the duplicates. A caller adds its validation to the builder returned.
    /// </summary>
    public static OptionsBuilder<StoreAuthenticationConfiguration> AddStoreAuthenticationOptions(this IServiceCollection services)
    {
        OptionsBuilder<StoreAuthenticationConfiguration> options = services.AddOptions<StoreAuthenticationConfiguration>();
        if (services.Any(service => service.ServiceType == typeof(StoreAuthenticationSection)))
            return options;

        services.AddSingleton(new StoreAuthenticationSection());
        return options.BindConfiguration(StoreAuthenticationSection.Path);
    }

    /// <summary>Marks the store authentication section as bound in a service collection.</summary>
    private sealed class StoreAuthenticationSection
    {
        public const string Path = "Application:StoreAuthentication";
    }

}
