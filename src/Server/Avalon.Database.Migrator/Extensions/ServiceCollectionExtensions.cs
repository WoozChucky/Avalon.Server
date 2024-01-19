using Avalon.Database.Migrator.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Avalon.Database.Migrator.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDatabaseMigrator(this IServiceCollection services, MigratorConfiguration configuration)
    {
        return services
            .AddSingleton(configuration)
            .AddSingleton<IDatabaseMigrator, DatabaseMigrator>();
    }
    
    public static IServiceCollection AddDatabaseMigrator(this IServiceCollection services)
    {
        return services
            .AddSingleton<IDatabaseMigrator, DatabaseMigrator>();
    }
}
