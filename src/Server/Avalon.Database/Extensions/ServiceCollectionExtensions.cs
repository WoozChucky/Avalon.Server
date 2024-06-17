using System;
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
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));
        
        return services
            .AddAuthDatabase(configuration)
            .AddCharactersDatabase(configuration)
            .AddWorldDatabase(configuration);
    }
    
    public static IServiceCollection AddAuthDatabase(this IServiceCollection services, DatabaseConfiguration configuration)
    {
        if (configuration.Auth == null)
            throw new ArgumentNullException(nameof(configuration.Auth));
        
        return services
            .AddScoped<IAuthDatabase, AuthDatabase>()
            .AddScoped<IAccountRepository, AccountRepository>(_ => new AccountRepository(GenerateConnectionString(configuration.Auth)))
            .AddScoped<IMFASetupRepository, MFASetupRepository>(_ => new MFASetupRepository(GenerateConnectionString(configuration.Auth)))
            .AddScoped<IDeviceRepository, DeviceRepository>(_ => new DeviceRepository(GenerateConnectionString(configuration.Auth)))
            .AddScoped<IWorldRepository, WorldRepository>(_ => new WorldRepository(GenerateConnectionString(configuration.Auth)));
    }
    
    public static IServiceCollection AddCharactersDatabase(this IServiceCollection services, DatabaseConfiguration configuration)
    {
        if (configuration.Characters == null)
            throw new ArgumentNullException(nameof(configuration.Characters));
        
        services
            .AddScoped<ICharactersDatabase, CharactersDatabase>()
            .AddScoped<ICharacterRepository, CharacterRepository>(_ => new CharacterRepository(GenerateConnectionString(configuration.Characters)));
        
        return services;
    }
    
    public static IServiceCollection AddWorldDatabase(this IServiceCollection services, DatabaseConfiguration configuration)
    {
        if (configuration.World == null)
            throw new ArgumentNullException(nameof(configuration.World));
        
        services
            .AddScoped<IWorldDatabase, WorldDatabase>()
            .AddScoped<IMapRepository, MapRepository>(_ => new MapRepository(GenerateConnectionString(configuration.World)))
            .AddScoped<ICreatureTemplateRepository, CreatureTemplateRepository>(_ => new CreatureTemplateRepository(GenerateConnectionString(configuration.World)))
            .AddScoped<IQuestTemplateRepository, QuestTemplateRepository>(_ => new QuestTemplateRepository(GenerateConnectionString(configuration.World)))
            .AddScoped<IQuestRewardRepository, QuestRewardRepository>(_ => new QuestRewardRepository(GenerateConnectionString(configuration.World)))
            .AddScoped<IQuestRewardTemplateRepository, QuestRewardTemplateRepository>(_ => new QuestRewardTemplateRepository(GenerateConnectionString(configuration.World)));
        
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
