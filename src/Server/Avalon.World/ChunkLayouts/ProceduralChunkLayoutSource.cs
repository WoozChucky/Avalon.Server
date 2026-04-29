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
    private readonly ProceduralLayoutGenerator _generator;
    private readonly Random _seedRng = new();
    private readonly object _seedLock = new();

    public ProceduralChunkLayoutSource(
        ILoggerFactory loggerFactory,
        IChunkLibrary library,
        IServiceScopeFactory scopeFactory)
    {
        _library = library;
        _scopeFactory = scopeFactory;
        _generator = new ProceduralLayoutGenerator(loggerFactory);
    }

    public async Task<ChunkLayout> BuildAsync(MapTemplate template, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var configRepo = scope.ServiceProvider.GetRequiredService<IProceduralMapConfigRepository>();
        var config = await configRepo.FindByTemplateIdAsync(template.Id, ct)
            ?? throw new InvalidProceduralConfigException(
                $"No ProceduralMapConfig for map {template.Id.Value}");

        var pool = _library.GetByPool(config.ChunkPoolId);
        return _generator.Generate(config, pool, NextSeed());
    }

    private int NextSeed()
    {
        lock (_seedLock) return _seedRng.Next();
    }
}
