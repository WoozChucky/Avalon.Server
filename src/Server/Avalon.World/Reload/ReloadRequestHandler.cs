using System.Text.Json;
using Avalon.Infrastructure;
using Avalon.World.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Avalon.World.Reload;

/// <summary>
/// Answers the API's reload requests (<see cref="CacheKeys.WorldReloadChannel"/>): parses the request, reloads the
/// areas it names and publishes the outcomes on the world's result channel under the request's id. A message that
/// cannot be read is logged and answered by nothing. Nothing here ever throws into the Redis subscriber.
/// </summary>
public sealed class ReloadRequestHandler(
    IReferenceDataReloader reloader,
    IReplicatedCache cache,
    IOptions<GameConfiguration> game,
    ILogger<ReloadRequestHandler> logger)
{
    /// <summary>The most of a rejected message that is logged: whoever can publish on the channel chooses its text.</summary>
    private const int MaxLoggedLength = 64;

    private readonly ushort _worldId = game.Value.WorldId.Value;

    /// <summary>The Redis callback: hands the message to the thread pool and returns at once.</summary>
    public void OnMessage(RedisChannel channel, RedisValue value)
    {
        string? text = value.ToString();
        _ = Task.Run(() => HandleAsync(text), CancellationToken.None);
    }

    /// <summary>Handles one message to completion. Never throws.</summary>
    public async Task HandleAsync(string? message)
    {
        try
        {
            ReloadRequestMessage? request = Parse(message);
            if (request is null)
            {
                return;
            }

            ReloadOutcomeMessage[] outcomes = await ReloadAsync(request.Areas).ConfigureAwait(false);
            string result = ReloadMessageJson.Serialize(new ReloadResultMessage(request.RequestId, outcomes));
            await cache.PublishAsync(CacheKeys.WorldReloadResultChannel(_worldId), result).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not answer a reload request on world {WorldId}", _worldId);
        }
    }

    private ReloadRequestMessage? Parse(string? message)
    {
        ReloadRequestMessage? request = null;
        try
        {
            request = string.IsNullOrWhiteSpace(message)
                ? null
                : JsonSerializer.Deserialize<ReloadRequestMessage>(message, ReloadMessageJson.Options);
        }
        catch (JsonException)
        {
            // Logged below with the rest of the malformed cases.
        }

        if (request is null || request.RequestId == Guid.Empty || request.Areas is null || request.Areas.Length == 0
            || request.Areas.Any(a => a is null))
        {
            string text = message ?? string.Empty;
            logger.LogWarning("Ignored a malformed reload request: {Message}",
                text.Length > MaxLoggedLength ? text[..MaxLoggedLength] : text);
            return null;
        }

        return request;
    }

    private async Task<ReloadOutcomeMessage[]> ReloadAsync(string[] requested)
    {
        string[] names = [.. requested.Distinct(StringComparer.Ordinal)];
        var areas = new Dictionary<string, ReloadArea>(StringComparer.Ordinal);
        foreach (string name in names)
        {
            if (Enum.TryParse(name, ignoreCase: false, out ReloadArea area) && Enum.IsDefined(area))
            {
                areas[name] = area;
            }
            else
            {
                logger.LogWarning("Refused a reload of the unknown area {Area}",
                    name.Length > MaxLoggedLength ? name[..MaxLoggedLength] : name);
            }
        }

        var byArea = new Dictionary<ReloadArea, ReloadOutcome>();
        string? failure = null;
        if (areas.Count > 0)
        {
            try
            {
                ReloadReport report = await reloader.ReloadAsync([.. areas.Values.Distinct()], CancellationToken.None)
                    .ConfigureAwait(false);
                foreach (ReloadOutcome outcome in report.Outcomes)
                {
                    byArea[outcome.Area] = outcome;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The reload of {Areas} on world {WorldId} threw", string.Join(",", areas.Keys), _worldId);
                failure = ex.GetType().Name;
            }
        }

        return [.. names.Select(name => Describe(name, areas, byArea, failure))];
    }

    /// <summary>
    /// A failed area. The API shows this beside a save that is already committed, so it says what the world kept
    /// and where the saved values are, not just that nothing changed.
    /// </summary>
    private static ReloadOutcomeMessage Kept(string area, string reason) => new(area, false,
        $"The world kept its previous {area} data: {reason}. " +
        "The saved values are in the database and load on the next successful reload or restart.");

    private static ReloadOutcomeMessage Describe(
        string name, Dictionary<string, ReloadArea> areas, Dictionary<ReloadArea, ReloadOutcome> byArea, string? failure)
    {
        if (!areas.TryGetValue(name, out ReloadArea area))
        {
            return new ReloadOutcomeMessage(name, false, "Unknown reload area.");
        }

        if (failure is not null)
        {
            return Kept(name, failure);
        }

        if (!byArea.TryGetValue(area, out ReloadOutcome? outcome))
        {
            return new ReloadOutcomeMessage(name, false, "The reload reported no outcome.");
        }

        return outcome.Succeeded
            ? new ReloadOutcomeMessage(name, true, outcome.Summary)
            : Kept(name, outcome.Error?.GetType().Name ?? "Reload failed");
    }
}
