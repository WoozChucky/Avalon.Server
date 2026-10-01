using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Scripts;

namespace Avalon.Api.Templates;

/// <summary>The script names a world has published (<see cref="CacheKeys.WorldScriptCatalog"/>).</summary>
public interface IWorldScriptCatalog
{
    /// <summary>
    /// The world's catalog, or null when it has published none: no build of it has reported in yet, or the stored
    /// value is not a catalog. Callers treat null as "do not check", never as "no scripts".
    /// </summary>
    Task<ScriptCatalogSnapshot?> GetAsync(WorldId world, CancellationToken ct);
}

/// <summary>Reads the catalog from Redis, only ever the named world's key.</summary>
public sealed class WorldScriptCatalog(IReplicatedCache cache) : IWorldScriptCatalog
{
    public async Task<ScriptCatalogSnapshot?> GetAsync(WorldId world, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string? json = await cache.GetAsync(CacheKeys.WorldScriptCatalog(world.Value));
        return string.IsNullOrWhiteSpace(json) ? null : ScriptCatalogJson.Deserialize(json);
    }
}
