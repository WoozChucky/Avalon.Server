using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Avalon.World.ChunkLayouts;

/// <summary>
/// Production layout source: loads <see cref="ProceduralMapConfig"/> + chunk pool from DB,
/// picks a seed, then delegates the deterministic generation to
/// <see cref="ProceduralLayoutGenerator"/> in <c>Avalon.World.Generation</c>. The pure
/// generator is also reused by Api admin tooling that previews layouts without DB writes.
/// </summary>
public class ProceduralChunkLayoutSource : IChunkLayoutSource
{
    private readonly IChunkLibrary _library;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISceneryCatalog _scenery;
    private readonly ProceduralLayoutGenerator _generator;
    private readonly Random _seedRng = new();
    private readonly object _seedLock = new();

    public ProceduralChunkLayoutSource(
        ILoggerFactory loggerFactory,
        IChunkLibrary library,
        IServiceScopeFactory scopeFactory,
        ISceneryCatalog scenery)
    {
        _library = library;
        _scopeFactory = scopeFactory;
        _scenery = scenery;
        _generator = new ProceduralLayoutGenerator(loggerFactory);
    }

    public async Task<ChunkLayout> BuildAsync(MapTemplate template, CancellationToken ct)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IProceduralMapConfigRepository configRepo = scope.ServiceProvider.GetRequiredService<IProceduralMapConfigRepository>();
        ProceduralMapConfig config = await configRepo.FindByTemplateIdAsync(template.Id, ct)
            ?? throw new InvalidProceduralConfigException(
                $"No ProceduralMapConfig for map {template.Id.Value}");

        IReadOnlyList<ChunkPoolMember> pool = _library.GetByPool(config.ChunkPoolId);
        ChunkLayout layout = _generator.Generate(config, pool, NextSeed(), _library.GetGroupsByPool(config.ChunkPoolId));

        // The map's scenery ring (#1110), built here, off the tick, as the instance is. A map without a ring file is
        // built without a ring.
        return _scenery.PoolFor(template.Id.Value) is { } scenery
            ? layout with { Scenery = SceneryRing.Build(layout, id => _library.GetById(id).Exits, scenery) }
            : layout;
    }

    private int NextSeed()
    {
        lock (_seedLock) return _seedRng.Next();
    }
}
