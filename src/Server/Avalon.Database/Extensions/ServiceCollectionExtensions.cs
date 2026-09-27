using Avalon.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Avalon.Database.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAvalonDatabases(this IServiceCollection services,
        string configurationSection = "Database")
    {
        services.AddOptions<DatabaseConfiguration>()
            .BindConfiguration(configurationSection);

        return services;
    }

    /// <summary>
    /// Validates at startup that every database in <paramref name="required"/> has a connection
    /// string (#543). Called by the host, not by <see cref="AddAvalonDatabases"/>, because each host
    /// opens a different set; the REST API validates its own database settings.
    /// </summary>
    public static IServiceCollection ValidateDatabasesOnStart(this IServiceCollection services,
        DatabaseConnections required, string configurationSection = "Database")
    {
        services.AddSingleton<IValidateOptions<DatabaseConfiguration>>(
            new DatabaseConnectionsValidation(configurationSection, required));
        services.AddOptions<DatabaseConfiguration>().ValidateOnStart();

        return services;
    }
}
