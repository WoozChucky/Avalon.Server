using Avalon.Domain.World;
using Avalon.World.Instances;
using Avalon.World.Maps.Navigation;
using Avalon.World.Public;
using DotRecast.Detour;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Avalon.World.ChunkLayouts;

public interface IChunkLayoutInstanceFactory
{
    /// <summary>
    /// Builds a <see cref="MapInstance"/> for <paramref name="template"/> by resolving the matching
    /// <see cref="IChunkLayoutSource"/> (predefined for towns, procedural for normals), baking a
    /// navmesh from the resulting <see cref="ChunkLayout"/>, and applying creature/portal placement.
    /// </summary>
    Task<MapInstance> BuildAsync(MapTemplate template, long? ownerAccountId, CancellationToken ct);
}

public class ChunkLayoutInstanceFactory : IChunkLayoutInstanceFactory
{
    private readonly ILoggerFactory _lf;
    private readonly ILogger<ChunkLayoutInstanceFactory> _logger;
    private readonly IChunkLayoutSourceResolver _resolver;
    private readonly IChunkLayoutNavmeshBuilder _navBuilder;
    private readonly ICreaturePlacementService _creaturePlace;
    private readonly IPortalPlacementService _portalPlace;
    private readonly IServiceProvider _sp;

    public ChunkLayoutInstanceFactory(
        ILoggerFactory lf,
        IChunkLayoutSourceResolver resolver,
        IChunkLayoutNavmeshBuilder navBuilder,
        ICreaturePlacementService creaturePlace,
        IPortalPlacementService portalPlace,
        IServiceProvider sp)
    {
        _lf = lf;
        _logger = lf.CreateLogger<ChunkLayoutInstanceFactory>();
        _resolver = resolver;
        _navBuilder = navBuilder;
        _creaturePlace = creaturePlace;
        _portalPlace = portalPlace;
        _sp = sp;
    }

    public async Task<MapInstance> BuildAsync(MapTemplate template, long? ownerAccountId, CancellationToken ct)
    {
        var source = _resolver.Resolve(template, out var kind);
        var layout = await source.BuildAsync(template, ct);

        DtNavMesh navMesh = await _navBuilder.BuildAsync(layout, ct);

        var navigator = new MapNavigator(_lf);
        navigator.LoadFromNavMesh(navMesh);

        var world = _sp.GetRequiredService<IWorld>();
        var instance = new MapInstance(_lf, _sp, world, template.Id, ownerAccountId, layout, navigator, layout.Seed, template.MapType);

        // Creatures only spawn on procedural maps; predefined town layouts leave Config null.
        if (kind == ChunkLayoutSourceKind.Procedural && layout.Config is { } cfg)
        {
            await _creaturePlace.PlaceAsync(instance, layout, cfg, layout.Seed, ct);
        }

        // PortalPlacementService tolerates a null config (towns) — portals are read off ChunkLayout.Portals.
        _portalPlace.Place(instance, layout, layout.Config);

        _logger.LogInformation("Built {Kind} instance {InstanceId} for map {MapId}",
            kind, instance.InstanceId, template.Id.Value);
        return instance;
    }
}
