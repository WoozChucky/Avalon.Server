using Avalon.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
}
