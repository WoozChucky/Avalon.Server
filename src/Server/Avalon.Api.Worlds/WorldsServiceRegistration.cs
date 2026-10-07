using Avalon.Api.Worlds.Authorization;
using Avalon.Api.Worlds.Balance;
using Avalon.Api.Worlds.Config;
using Avalon.Api.Worlds.Controllers;
using Avalon.Api.Worlds.Previews;
using Avalon.Api.Worlds.Services;
using Avalon.Api.Worlds.Templates;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Avalon.Api.Worlds;

public static class WorldsServiceRegistration
{
    /// <summary>
    /// What the worlds service registers, as Avalon.Api registered it before the split (#794), its exception mapper
    /// aside: its settings, the template editing, the balance workbench's client, its services and the character
    /// authorization handlers. <see cref="WorldsConfig"/> is read from <paramref name="configuration"/> here, so a public
    /// site URL that is not one stops startup. The shared hosting registers what these run on: the world databases and
    /// their repositories, Redis.
    /// </summary>
    public static IServiceCollection AddWorlds(this IServiceCollection services, IConfiguration configuration)
    {
        var config = WorldsConfig.Bind(configuration);

        services.AddSingleton(new PublicWorldSettings(config.PublicWorldId));
        services.AddSingleton(PublicSiteSettings.Create(config.PublicSiteUrl));
        services.AddOptions<PreviewConfiguration>().BindConfiguration("Application:Previews");
        // Observability's reads of a presence's own world, the layout inputs included.
        services.AddSingleton<IWorldContentRepositories, WorldContentRepositories>();

        services.AddOptions<TemplateEditingOptions>()
            .BindConfiguration(TemplateEditingOptions.Section)
            .ValidateOnStart();
        services.AddTemplateEditing();
        services.AddSingleton<IValidateOptions<TemplateEditingOptions>, TemplateEditingOptionsValidator>();

        services.AddOptions<MapAssetConfig>()
            .BindConfiguration("Application:MapAssets");

        services.AddMemoryCache();

        // The balance workbench's service (in-cluster). Without it the /balance endpoints answer 503.
        BalanceConfiguration balance = config.Balance ?? new();
        if (balance.IsConfigured)
            services.AddBalanceClient(balance);
        else
            services.AddSingleton<IBalanceClient, UnconfiguredBalanceClient>();

        services.AddScoped<ICharacterService, CharacterService>();
        services.AddScoped<IAccountCharactersService, AccountCharactersService>();
        services.AddScoped<IWorldService, WorldService>();
        services.AddScoped<IMapService, MapService>();
        services.AddScoped<IProceduralLayoutInputsResolver, ProceduralLayoutInputsResolver>();
        services.AddScoped<IObservabilityService, ObservabilityService>();
        // A world's ready heartbeat, which identity's game admission reads too: registered once in a process.
        services.TryAddSingleton<IWorldReadiness, WorldReadiness>();
        services.AddWorldMaintenanceControl();

        // A character's owner, or a game master to read it and an admin to write it.
        services.AddScoped<IAuthorizationHandler, CharacterReadHandler>();
        services.AddScoped<IAuthorizationHandler, CharacterWriteHandler>();
        return services;
    }
}
