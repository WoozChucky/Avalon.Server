using Avalon.Configuration;
using Avalon.Database.Auth;
using Avalon.Database.Characters;
using Avalon.Database.World;
using Microsoft.Extensions.DependencyInjection;

namespace Avalon.Database.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDatabases(this IServiceCollection services, DatabaseConfiguration configuration)
    {
        services.AddScoped<IAuthDatabase, AuthDatabase>(_ => new AuthDatabase(configuration.Auth));
        services.AddScoped<ICharactersDatabase, CharactersDatabase>(_ => new CharactersDatabase(configuration.Characters));
        services.AddScoped<IWorldDatabase, WorldDatabase>(_ => new WorldDatabase(configuration.World));
        
        return services;
    }
}