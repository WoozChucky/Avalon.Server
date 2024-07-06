using Avalon.Auth.Database.Extensions;
using Avalon.Database.Character.Extensions;
using Avalon.Game.Configuration;
using Avalon.Infrastructure.Extensions;
using Avalon.World;
using Avalon.World.Database.Extensions;
using Avalon.World.Entities;
using Avalon.World.Maps;
using Avalon.World.Pools;
using Avalon.World.Quests;
using Avalon.World.Scripts;
using Microsoft.Extensions.DependencyInjection;

namespace Avalon.Server.World.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddWorldServices(this IServiceCollection services)
    {
        services
            .AddOptions<GameConfiguration>()
            .BindConfiguration("Game");
        
        services
            .AddAuthDatabase() //TODO: World should not depend on Auth database
            .AddCharacterDatabase()
            .AddWorldDatabase()
            .AddCache();

        services.AddSingleton<IWorld, Avalon.World.World>();
        services.AddSingleton<IAvalonMapManager, AvalonMapManager>();
        services.AddSingleton<IPoolManager, PoolManager>();
        services.AddSingleton<IAiController, AiController>();
        services.AddSingleton<ICreatureSpawner, CreatureSpawner>();
        
        //services.AddSingleton<IQuestManager, QuestManager>();
        
        return services;
    }
}
