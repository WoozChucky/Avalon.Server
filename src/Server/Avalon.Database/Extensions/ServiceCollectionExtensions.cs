using Avalon.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Avalon.Database.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAvalonDatabases(this IServiceCollection services, string configurationSection = "Database")
    {
        services.AddOptions<DatabaseConfiguration>()
            .BindConfiguration(configurationSection);

        return services;
    }

    private static string GenerateConnectionString(DatabaseConnection configuration)
    {
        var connectionString = $"Server={configuration.Host};" +
                               $"Port={configuration.Port};" +
                               $"Database={configuration.Database};" +
                               $"userid={configuration.Username};" +
                               $"Pwd={configuration.Password};" +
                               $"ConvertZeroDatetime=True;" +
                               $"AllowZeroDateTime=True";
        return connectionString;
    }
}
