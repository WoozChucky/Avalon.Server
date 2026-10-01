using Avalon.Infrastructure;
using Avalon.Infrastructure.Scripts;
using Avalon.World.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Avalon.World.Scripts;

/// <summary>
/// Writes the script names this world accepts to <see cref="CacheKeys.WorldScriptCatalog"/>, for the Api to offer
/// the admin app and to check template saves against. No expiry: the value stays true until this world publishes
/// again, which it does after its scripts load and after every hot reload. A failed write is logged and never thrown:
/// a world must start without the catalog, and the next publish repairs it.
/// </summary>
public sealed class ScriptCatalogPublisher(
    IScriptManager scripts,
    IReplicatedCache cache,
    IOptions<GameConfiguration> game,
    ILogger<ScriptCatalogPublisher> logger)
{
    private readonly ushort _worldId = game.Value.WorldId.Value;

    public async Task PublishAsync()
    {
        try
        {
            ScriptCatalogSnapshot snapshot = new(
                scripts.AiScriptNames.ToArray(), scripts.AbilityScriptNames.ToArray(), scripts.QuestScriptNames.ToArray());
            await cache.SetAsync(CacheKeys.WorldScriptCatalog(_worldId), ScriptCatalogJson.Serialize(snapshot), null)
                .ConfigureAwait(false);
            logger.LogInformation(
                "Published the script catalog of world {WorldId}: {Ai} AI, {Ability} ability, {Quest} quest scripts",
                _worldId, snapshot.Ai.Count, snapshot.Ability.Count, snapshot.Quest.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not publish the script catalog of world {WorldId}", _worldId);
        }
    }
}
