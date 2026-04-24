using Avalon.Domain.World;
using Avalon.World.Instances;
using Avalon.World.Maps.Navigation;
using Avalon.World.Public;
using DotRecast.Detour;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Procedural;

public interface IProceduralInstanceFactory
{
    Task<MapInstance> CreateAsync(MapTemplate template, ProceduralMapConfig config, long? ownerAccountId, int seed, CancellationToken ct);
}

public class ProceduralInstanceFactory : IProceduralInstanceFactory
{
    private readonly ILoggerFactory _lf;
    private readonly ILogger<ProceduralInstanceFactory> _logger;
    private readonly IChunkLibrary _library;
    private readonly IProceduralLayoutGenerator _layoutGen;
    private readonly IProceduralNavmeshBuilder _navBuilder;
    private readonly ICreaturePlacementService _creaturePlace;
    private readonly IPortalPlacementService _portalPlace;
    private readonly IServiceProvider _sp;

    public ProceduralInstanceFactory(
        ILoggerFactory lf,
        IChunkLibrary library,
        IProceduralLayoutGenerator layoutGen,
        IProceduralNavmeshBuilder navBuilder,
        ICreaturePlacementService creaturePlace,
        IPortalPlacementService portalPlace,
        IServiceProvider sp)
    {
        _lf = lf;
        _logger = lf.CreateLogger<ProceduralInstanceFactory>();
        _library = library;
        _layoutGen = layoutGen;
        _navBuilder = navBuilder;
        _creaturePlace = creaturePlace;
        _portalPlace = portalPlace;
        _sp = sp;
    }

    public async Task<MapInstance> CreateAsync(MapTemplate template, ProceduralMapConfig config, long? ownerAccountId, int seed, CancellationToken ct)
    {
        var pool = _library.GetByPool(config.ChunkPoolId);
        var layout = _layoutGen.Generate(config, pool, seed);

        DtNavMesh navMesh = await _navBuilder.BuildAsync(layout, ct);

        var navigator = new MapNavigator(_lf);
        navigator.LoadFromNavMesh(navMesh);

        var world = _sp.GetRequiredService<IWorld>();
        var instance = new MapInstance(_lf, _sp, world, template.Id, ownerAccountId, layout, navigator, seed);

        await _creaturePlace.PlaceAsync(instance, layout, config, seed, ct);
        _portalPlace.Place(instance, layout, config);

        _logger.LogInformation("Built procedural instance {InstanceId} for map {MapId} seed {Seed}",
            instance.InstanceId, template.Id.Value, seed);
        return instance;
    }
}
