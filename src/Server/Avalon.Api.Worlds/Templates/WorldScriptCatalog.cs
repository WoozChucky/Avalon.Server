using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Scripts;
using StackExchange.Redis;

namespace Avalon.Api.Worlds.Templates;

/// <summary>The script names a world has published (<see cref="CacheKeys.WorldScriptCatalog"/>).</summary>
public interface IWorldScriptCatalog
{
    /// <summary>
    /// The world's catalog, or null when it has published none: no build of it has reported in yet, the stored
    /// value is not a catalog, or Redis could not be read. Callers treat null as "do not check", never as "no scripts".
    /// </summary>
    Task<ScriptCatalogSnapshot?> GetAsync(WorldId world, CancellationToken ct);
}

/// <summary>Reads the catalog from Redis, only ever the named world's key.</summary>
public sealed class WorldScriptCatalog(IReplicatedCache cache, ILogger<WorldScriptCatalog> logger) : IWorldScriptCatalog
{
    public async Task<ScriptCatalogSnapshot?> GetAsync(WorldId world, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            string? json = await cache.GetAsync(CacheKeys.WorldScriptCatalog(world.Value));
            return string.IsNullOrWhiteSpace(json) ? null : ScriptCatalogJson.Deserialize(json);
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException) // a timeout is not a RedisException
        {
            // Redis down or slow, or the key holding something that is not a string. A template save worked without
            // Redis before the catalog existed and still must: this is "not published". Only the world is logged,
            // not the exception text, which can carry the key and the value.
            logger.LogWarning("The script catalog of world {WorldId} could not be read from Redis; treating it as not published",
                world.Value);
            return null;
        }
    }
}
