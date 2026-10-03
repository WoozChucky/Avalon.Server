using System.Collections.Concurrent;
using System.Text.Json;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Avalon.Api.Templates;

/// <summary>
/// The part of a world's static data a template save changes. The names are the ones the reload request carries on
/// the wire, so the signal that sends it maps them to the world's own reload areas by name.
/// </summary>
public enum TemplateReloadArea
{
    Items,
    Abilities,
    Creatures,
    Auras,
}

/// <param name="Status"><c>applied</c>, <c>failed</c> or <c>pending</c> (the world did not answer in time).</param>
/// <param name="Summary">The world's own words on the outcome; null when it said none.</param>
public sealed record TemplateReloadResult(string Status, string? Summary)
{
    public const string Applied = "applied";
    public const string Failed = "failed";
    public const string Pending = "pending";
}

/// <summary>Asks a world to reload part of its static data after a template save, and reports how that went.</summary>
public interface ITemplateReloadSignal
{
    /// <summary>
    /// Returns once the world answers or the wait runs out. A save is already committed when this is called, so an
    /// outcome other than <c>applied</c> never undoes it.
    /// </summary>
    Task<TemplateReloadResult> RequestAsync(WorldId world, TemplateReloadArea area, CancellationToken ct);
}

/// <summary>
/// Asks the world over Redis. Subscribes once, on first use, to the result channel of each editable world, and
/// correlates answers by request id: each request owns a <see cref="TaskCompletionSource{TResult}"/>, created before
/// the request is published so a fast answer cannot be missed. An answer for an id that is not pending (a request
/// that timed out, or another API instance's) is ignored.
/// </summary>
public sealed class RedisTemplateReloadSignal(
    IReplicatedCache cache,
    IOptions<TemplateEditingOptions> options,
    TimeProvider time,
    ILogger<RedisTemplateReloadSignal> logger) : ITemplateReloadSignal
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<ReloadResultMessage>> _pending = new();
    private readonly SemaphoreSlim _subscribeLock = new(1, 1);
    private volatile bool _subscribed;

    public async Task<TemplateReloadResult> RequestAsync(WorldId world, TemplateReloadArea area, CancellationToken ct)
    {
        await EnsureSubscribedAsync(world).ConfigureAwait(false);

        var requestId = Guid.NewGuid();
        var answer = new TaskCompletionSource<ReloadResultMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = answer;
        try
        {
            string message = ReloadMessageJson.Serialize(new ReloadRequestMessage(requestId, [area.ToString()]));
            await cache.PublishAsync(CacheKeys.WorldReloadChannel(world.Value), message).ConfigureAwait(false);

            // The timeout runs on the injected clock; the caller's token ends the wait early. Neither throws out.
            using var timeout = new CancellationTokenSource(options.Value.ReloadTimeout, time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, ct);
            try
            {
                ReloadResultMessage result = await answer.Task.WaitAsync(linked.Token).ConfigureAwait(false);
                return Describe(result);
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning("World {WorldId} did not answer the {Area} reload request {RequestId} in time",
                    world.Value, area, requestId);
                return new TemplateReloadResult(TemplateReloadResult.Pending, null);
            }
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    private static TemplateReloadResult Describe(ReloadResultMessage result)
    {
        ReloadOutcomeMessage[] outcomes = result.Outcomes ?? [];
        if (outcomes.Length == 0)
        {
            return new TemplateReloadResult(TemplateReloadResult.Failed, "The world reported no outcome.");
        }

        string[] failed = [.. outcomes.Where(o => !o.Succeeded).Select(o => o.Summary)];
        if (failed.Length > 0)
        {
            return new TemplateReloadResult(TemplateReloadResult.Failed, string.Join("; ", failed));
        }

        string summary = string.Join("; ", outcomes.Select(o => o.Summary).Where(s => !string.IsNullOrEmpty(s)));
        return new TemplateReloadResult(TemplateReloadResult.Applied, summary.Length == 0 ? null : summary);
    }

    private async Task EnsureSubscribedAsync(WorldId world)
    {
        if (!options.Value.IsEditable(world))
        {
            logger.LogWarning("A reload was requested of world {WorldId}, which is not an editable world", world.Value);
        }

        if (_subscribed)
        {
            return;
        }

        await _subscribeLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_subscribed)
            {
                return;
            }

            // Only editable worlds are asked; a failed subscribe leaves the flag down so the next save retries.
            foreach (ushort id in options.Value.EditableWorlds)
            {
                await cache.SubscribeAsync(CacheKeys.WorldReloadResultChannel(id), OnResult).ConfigureAwait(false);
            }

            _subscribed = true;
        }
        finally
        {
            _subscribeLock.Release();
        }
    }

    private void OnResult(RedisChannel channel, RedisValue value)
    {
        ReloadResultMessage? result;
        try
        {
            result = JsonSerializer.Deserialize<ReloadResultMessage>(value.ToString(), ReloadMessageJson.Options);
        }
        catch (JsonException)
        {
            logger.LogWarning("Ignored a reload result that is not valid JSON on {Channel}", channel.ToString());
            return;
        }

        if (result is not null && _pending.TryGetValue(result.RequestId, out var answer))
        {
            answer.TrySetResult(result);
        }
    }
}
