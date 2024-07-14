using Avalon.Configuration;
using Avalon.Database.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Avalon.Auth.Database.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddAuthDatabase(this IServiceCollection services, string databaseSection = "Database")
    {
        services.AddAvalonDatabases(databaseSection);
        services.AddScoped<AuthDbContext>(provider =>
        {
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var options = provider.GetRequiredService<IOptionsSnapshot<DatabaseConfiguration>>();
            return new AuthDbContext(loggerFactory, options);
        });

        services
            .AddScoped<Repositories.IAccountRepository, Repositories.AccountRepository>()
            .AddScoped<Repositories.IMfaSetupRepository, Repositories.MfaSetupRepository>()
            .AddScoped<Repositories.IDeviceRepository, Repositories.DeviceRepository>()
            .AddScoped<Repositories.IWorldRepository, Repositories.WorldRepository>();

        return services;
    }
}
