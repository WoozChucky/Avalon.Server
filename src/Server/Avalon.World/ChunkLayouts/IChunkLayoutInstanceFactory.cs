using Avalon.Domain.World;
using Avalon.World.Doodads;
using Avalon.World.Instances;
using Avalon.World.Maps.Navigation;
using Avalon.World.Parties;
using DotRecast.Detour;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Avalon.World.ChunkLayouts;

public interface IChunkLayoutInstanceFactory
{
    /// <summary>
    /// Builds a <see cref="MapInstance"/> for <paramref name="template"/> by resolving the matching
    /// <see cref="IChunkLayoutSource"/> (predefined for towns, procedural for normals), baking a
    /// navmesh from the resulting <see cref="ChunkLayout"/>, and applying creature, doodad and portal placement.
    /// </summary>
    Task<MapInstance> BuildAsync(MapTemplate template, uint? ownerCharacterId, CancellationToken ct,
        PartyId? ownerPartyId = null);
}

public class ChunkLayoutInstanceFactory : IChunkLayoutInstanceFactory
{
    private readonly ILoggerFactory _lf;
    private readonly ILogger<ChunkLayoutInstanceFactory> _logger;
    private readonly IChunkLayoutSourceResolver _resolver;
    private readonly IChunkLayoutNavmeshBuilder _navBuilder;
    private readonly ICreaturePlacementService _creaturePlace;
    private readonly IPortalPlacementService _portalPlace;
    private readonly IDoodadPlacementService _doodadPlace;
    private readonly IServiceProvider _sp;

    public ChunkLayoutInstanceFactory(
        ILoggerFactory lf,
        IChunkLayoutSourceResolver resolver,
        IChunkLayoutNavmeshBuilder navBuilder,
        ICreaturePlacementService creaturePlace,
        IPortalPlacementService portalPlace,
        IDoodadPlacementService doodadPlace,
        IServiceProvider sp)
    {
        _lf = lf;
        _logger = lf.CreateLogger<ChunkLayoutInstanceFactory>();
        _resolver = resolver;
        _navBuilder = navBuilder;
        _creaturePlace = creaturePlace;
        _portalPlace = portalPlace;
        _doodadPlace = doodadPlace;
        _sp = sp;
    }

    public async Task<MapInstance> BuildAsync(MapTemplate template, uint? ownerCharacterId, CancellationToken ct,
        PartyId? ownerPartyId = null)
    {
        IChunkLayoutSource source = _resolver.Resolve(template, out ChunkLayoutSourceKind kind);
        ChunkLayout layout = await source.BuildAsync(template, ct);
        IWorld world = _sp.GetRequiredService<IWorld>();

        // Before the bake, from the layout alone (the solid doodads' boxes are the bake's to cut out); a town, with no
        // config, plans none.
        DoodadPlan doodads = _doodadPlace.Plan(layout, layout.Config, world.Data?.Doodads ?? DoodadCatalog.Empty,
            _sp.GetRequiredService<IChunkLibrary>());

        // The bake cuts the solid doodads' boxes out, and the instance keeps this layout, so its layout packet carries
        // them to the client. A layout with none stays the one its source built.
        if (doodads.Obstacles.Count > 0)
            layout = layout with { Obstacles = doodads.Obstacles };

        DtNavMesh navMesh = await _navBuilder.BuildAsync(layout, ct);

        var navigator = new MapNavigator(_lf);
        navigator.LoadFromNavMesh(navMesh);

        var instance = new MapInstance(_lf, _sp, world, template.Id, ownerCharacterId, layout, navigator, layout.Seed,
            template.MapType, ownerPartyId: ownerPartyId);

        try
        {
            // Spawn-table placement needs a config and chunk spawn slots, so it is procedural-only;
            // predefined town layouts leave Config null.
            if (kind == ChunkLayoutSourceKind.Procedural && layout.Config is { } cfg)
                await _creaturePlace.PlaceAsync(instance, layout, cfg, layout.Seed, ct);

            // Authored placement runs for every layout kind. It is the only path that puts creatures in
            // a town, and procedural maps are free to use it too — a map with no MapCreatureSpawn rows
            // places nothing.
            await _creaturePlace.PlaceAuthoredAsync(instance, layout, template.Id, ct);

            // On the baked mesh, before the instance is published: the only time a doodad may be added.
            await _doodadPlace.PlaceAsync(instance, layout, doodads, template.Id, ct);

            // PortalPlacementService tolerates a null config (towns) — portals are read off ChunkLayout.Portals.
            _portalPlace.Place(instance, layout, layout.Config);

            _logger.LogInformation("Built {Kind} instance {InstanceId} for map {MapId}",
                kind, instance.InstanceId, template.Id.Value);
            return instance;
        }
        catch
        {
            // The registry never receives a failed build, so the factory must release its cached layout (#1130).
            instance.Dispose();
            throw;
        }
    }
}
