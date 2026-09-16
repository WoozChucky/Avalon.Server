using Avalon.Configuration;
using Avalon.Database.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Avalon.Database.Auth.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddAuthDatabase(this IServiceCollection services, string databaseSection = "Database")
    {
        services.AddAvalonDatabases(databaseSection);

        // The context itself is not registered: repositories create one per call and nothing else
        // may hold one. IOptions, not IOptionsSnapshot — the factory is a singleton.
        services.AddSingleton<IDbContextFactory<AuthDbContext>>(provider =>
        {
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var options = provider.GetRequiredService<IOptions<DatabaseConfiguration>>();
            return new DelegateDbContextFactory<AuthDbContext>(() => new AuthDbContext(loggerFactory, options));
        });
        services.AddSingleton<IDbTransactionRunner<AuthDbContext>, DbTransactionRunner<AuthDbContext>>();

        services
            .AddSingleton<Repositories.IAccountRepository, Repositories.AccountRepository>()
            .AddSingleton<Repositories.IMfaSetupRepository, Repositories.MfaSetupRepository>()
            .AddSingleton<Repositories.IDeviceRepository, Repositories.DeviceRepository>()
            .AddSingleton<Repositories.IWorldRepository, Repositories.WorldRepository>()
            .AddSingleton<Repositories.IPersonalAccessTokenRepository, Repositories.PersonalAccessTokenRepository>()
            .AddSingleton<Repositories.IRefreshTokenRepository, Repositories.RefreshTokenRepository>();

        return services;
    }
}
