using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Avalon.LoadTest.Ramp;

/// <summary>
/// The server-side values a ramp step is judged on, read from Prometheus at the step's end. Null is a value
/// Prometheus could not give (an empty result, the server unreachable, NaN), so the step cannot be judged on it;
/// <see cref="Drops"/> reads an empty series as 0 and is NaN only when its query failed.
/// </summary>
/// <param name="TickP99Ms">The 99th percentile of tick duration, in milliseconds.</param>
/// <param name="Tps">The average tick rate, ticks per second.</param>
/// <param name="Drops">Outbound packets the server dropped (a client's outbox full) over the window.</param>
/// <param name="ReceiveBacklogMax">The deepest the receive queue got over the window.</param>
/// <param name="WorkingSetFraction">The world process's working set as a fraction of its pod's memory limit.</param>
/// <param name="WorkingSetMb">The world process's working set, in MiB.</param>
/// <param name="Gen2PerMin">Gen 2 collections per minute over the window.</param>
/// <param name="GcPauseFraction">The fraction of the window the GC paused the process.</param>
/// <param name="SaveP95Ms">
/// The 95th percentile of character save duration, in milliseconds; 0 when the window is known to have had no save,
/// null when it had saves but no percentile came back or whether it had any cannot be told (too few samples, a failed
/// query).
/// </param>
/// <param name="Instances">Map instances active at the step's end.</param>
public sealed record ServerValues(
    double? TickP99Ms, double? Tps, double Drops, double? ReceiveBacklogMax, double? WorkingSetFraction, double? WorkingSetMb,
    double? Gen2PerMin, double? GcPauseFraction, double? SaveP95Ms, double? Instances);

/// <summary>Prometheus could not answer a query the run needs.</summary>
public sealed class PrometheusException(string message) : Exception(message);

/// <summary>
/// Instant queries against Prometheus's HTTP API for one world server: the limits' series (labelled
/// <c>avalon_world_id</c>, pushed by the world's OpenTelemetry exporter), its pod's memory limit (kube-state-metrics,
/// by pod name), its version and its players online. Read-only.
/// </summary>
public sealed class PrometheusClient(Uri baseUri, ushort worldId, string pod) : IDisposable
{
    /// <summary>The world server's container in its pod: the <c>avalon-world</c> chart names it after the chart.</summary>
    private const string Container = "avalon-world";

    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _http = new()
    {
        BaseAddress = baseUri.AbsolutePath.EndsWith('/') ? baseUri : new Uri(baseUri.AbsoluteUri + "/"),
        Timeout = s_timeout,
    };

    private readonly string _world = $"avalon_world_id=\"{worldId.ToString(CultureInfo.InvariantCulture)}\"";

    /// <summary>
    /// The server values over <paramref name="window"/> (whole seconds) ending at <paramref name="at"/>. The rates need
    /// two samples of a series in the window: with the exporter's default 60 s interval a 60 s window has one, and those
    /// values come back null.
    /// </summary>
    public async Task<ServerValues> SampleAsync(DateTimeOffset at, TimeSpan window, CancellationToken ct)
    {
        int w = Math.Max(1, (int)window.TotalSeconds);
        string range = $"[{w.ToString(CultureInfo.InvariantCulture)}s]";
        string world = _world;

        Task<double?> tick = ValueAsync(
            $"histogram_quantile(0.99, sum by (le)(rate(world_tick_duration_microseconds_bucket{{{world}}}{range}))) / 1000", at, ct);
        Task<double?> tps = ValueAsync($"avg_over_time(world_tick_rate_tps{{{world}}}{range})", at, ct, lowerIsWorse: true);
        Task<double?> drops = ValueAsync($"sum(increase(network_out_dropped_total{{{world}}}{range})) or vector(0)", at, ct);
        Task<double?> backlog = ValueAsync($"max_over_time(world_receive_queue_depth{{{world},stat=\"max\"}}{range})", at, ct);
        Task<double?> workingSet = ValueAsync($"max(dotnet_process_memory_working_set_bytes{{{world}}})", at, ct);
        Task<double?> workingSetFraction = ValueAsync(
            $"max(dotnet_process_memory_working_set_bytes{{{world}}}) / " +
            $"max(kube_pod_container_resource_limits{{namespace=\"avalon\",pod=\"{pod}\",container=\"{Container}\",resource=\"memory\"}})", at, ct);
        Task<double?> gen2 = ValueAsync(
            $"sum(increase(dotnet_gc_collections_total{{{world},gc_heap_generation=\"gen2\"}}{range})) * 60 / {w.ToString(CultureInfo.InvariantCulture)}",
            at, ct);
        Task<double?> gcPause = ValueAsync($"sum(rate(dotnet_gc_pause_time_seconds_total{{{world}}}{range}))", at, ct);
        Task<double?> save = ValueAsync(
            $"histogram_quantile(0.95, sum by (le)(rate(world_character_save_duration_milliseconds_bucket{{{world}}}{range})))",
            at, ct);
        // Whether the window had saves: the count's increase (two samples of a series in the window at least), its
        // samples in the window, whether it was there at the window's start, and whether a series of it appeared within
        // the window (one that a restarted world starts beside the old one; its first sample's saves are no increase).
        const string SaveCount = "world_character_save_duration_milliseconds_count";
        string windowSeconds = $"{w.ToString(CultureInfo.InvariantCulture)}s";
        Task<Answer> saveIncrease = AnswerAsync($"sum(increase({SaveCount}{{{world}}}{range}))", at, ct);
        Task<Answer> saveSamples = AnswerAsync($"sum(count_over_time({SaveCount}{{{world}}}{range}))", at, ct);
        Task<Answer> savesAtStart = AnswerAsync($"sum({SaveCount}{{{world}}})", at - TimeSpan.FromSeconds(w), ct);
        Task<Answer> newSaveSeries = AnswerAsync(
            $"count(count_over_time({SaveCount}{{{world}}}{range}) unless {SaveCount}{{{world}}} offset {windowSeconds}) > 0",
            at, ct);
        Task<double?> instances = ValueAsync($"avalon_world_instances_active{{{world}}}", at, ct);

        await Task.WhenAll(tick, tps, drops, backlog, workingSet, workingSetFraction, gen2, gcPause, save, saveIncrease,
            saveSamples, savesAtStart, newSaveSeries, instances);

        double? saveP95 = SaveP95(tick.Result, save.Result, saveIncrease.Result, saveSamples.Result, savesAtStart.Result,
            newSaveSeries.Result);

        return new ServerValues(
            tick.Result, tps.Result, drops.Result ?? double.NaN, backlog.Result, workingSetFraction.Result,
            workingSet.Result is { } bytes ? bytes / (1024 * 1024) : null, gen2.Result, gcPause.Result, saveP95, instances.Result);
    }

    /// <summary>The world server's version (<c>target_info</c>'s <c>service_version</c>); null when Prometheus has none.</summary>
    public async Task<string?> ServerVersionAsync(CancellationToken ct)
    {
        try
        {
            JsonArray results = await QueryAsync($"target_info{{{_world}}}", DateTimeOffset.UtcNow, ct);
            return results.Select(result => result?["metric"]?["service_version"]?.ToString())
                .FirstOrDefault(version => !string.IsNullOrEmpty(version));
        }
        catch (PrometheusException)
        {
            return null;
        }
    }

    /// <summary>The world's players online now.</summary>
    /// <exception cref="PrometheusException">Prometheus is unreachable or has no such series for the world.</exception>
    public Task<int> PlayersOnlineAsync(CancellationToken ct) => PlayersOnlineAsync(DateTimeOffset.UtcNow, ct);

    /// <summary>The world's players online at <paramref name="at"/>.</summary>
    /// <exception cref="PrometheusException">Prometheus is unreachable or has no such series for the world.</exception>
    public async Task<int> PlayersOnlineAsync(DateTimeOffset at, CancellationToken ct)
    {
        double? players = await ValueAsync($"avalon_world_players_online{{{_world}}}", at, ct, quiet: false);
        return players is { } value && double.IsFinite(value)
            ? (int)Math.Round(value)
            : throw new PrometheusException($"Prometheus has no avalon_world_players_online for world {worldId}.");
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    /// <summary>
    /// The save p95 a step is judged on. 0 only when the window is known to have had no save: the world never saved
    /// (no save count, before the window or in it), or the count did not move over samples spanning the window and no
    /// series of it appeared within the window. Null (unknown) when the window lacks samples (no tick value either),
    /// when a query failed, when the count has fewer than two samples in the window, or when saves happened (the count
    /// rose, or a series of it first appeared within the window, which takes a save) and the quantile is empty or NaN.
    /// Otherwise the quantile.
    /// </summary>
    private static double? SaveP95(double? tick, double? quantile, Answer increase, Answer samples, Answer atStart,
        Answer newSeries)
    {
        if (tick is null || !increase.Answered || !samples.Answered || !atStart.Answered || !newSeries.Answered)
            return null;

        double? p95 = quantile is { } q && double.IsFinite(q) ? q : null;
        if (samples.Value is not > 0)
        {
            // No sample in the window: a world that never saved is no save; a count that was there and stopped
            // reporting says nothing.
            return atStart.Value is null ? 0 : null;
        }

        if (atStart.Value is null || newSeries.Value > 0) return p95;
        return increase.Value switch
        {
            null => null,
            > 0 => p95,
            _ => 0,
        };
    }

    /// <summary>
    /// The query's value as <see cref="ValueAsync"/> reads it, with whether Prometheus answered at all: an empty result
    /// (<see cref="Answer.Value"/> null) is then told apart from a failed query.
    /// </summary>
    private async Task<Answer> AnswerAsync(string query, DateTimeOffset at, CancellationToken ct)
    {
        try
        {
            return new Answer(true, await ValueAsync(query, at, ct, quiet: false));
        }
        catch (PrometheusException)
        {
            return new Answer(false, null);
        }
    }

    /// <summary>
    /// The query's value: the highest across the result's series (the lowest with <paramref name="lowerIsWorse"/>),
    /// null when the result is empty or, unless <paramref name="quiet"/> is false, the query failed. A series reading NaN
    /// is passed over.
    /// </summary>
    private async Task<double?> ValueAsync(string query, DateTimeOffset at, CancellationToken ct, bool lowerIsWorse = false,
        bool quiet = true)
    {
        JsonArray results;
        try
        {
            results = await QueryAsync(query, at, ct);
        }
        catch (PrometheusException) when (quiet)
        {
            return null;
        }

        double? worst = null;
        foreach (JsonNode? result in results)
        {
            if (result?["value"] is not JsonArray { Count: 2 } pair ||
                !double.TryParse(pair[1]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                continue;
            }

            if (double.IsNaN(value)) continue;

            worst = worst is not { } current ? value : lowerIsWorse ? Math.Min(current, value) : Math.Max(current, value);
        }

        return worst;
    }

    /// <summary>The <c>data.result</c> vector of an instant query at <paramref name="at"/>.</summary>
    private async Task<JsonArray> QueryAsync(string query, DateTimeOffset at, CancellationToken ct)
    {
        string time = (at.ToUnixTimeMilliseconds() / 1000.0).ToString("0.###", CultureInfo.InvariantCulture);
        string path = $"api/v1/query?query={Uri.EscapeDataString(query)}&time={time}";
        try
        {
            using HttpResponseMessage response = await _http.GetAsync(path, ct);
            string text = await response.Content.ReadAsStringAsync(ct);
            var reply = JsonNode.Parse(text);
            if (!response.IsSuccessStatusCode || reply is null || reply["status"]?.ToString() != "success")
            {
                throw new PrometheusException(
                    $"Prometheus refused a query ({(int)response.StatusCode}): {reply?["error"]?.ToString() ?? "no detail"}.");
            }

            return reply["data"]?["result"] as JsonArray ?? [];
        }
        catch (HttpRequestException error)
        {
            throw new PrometheusException($"Prometheus at {_http.BaseAddress} cannot be reached: {error.Message}");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new PrometheusException($"Prometheus at {_http.BaseAddress} did not answer within {s_timeout.TotalSeconds:0} s.");
        }
        catch (JsonException)
        {
            throw new PrometheusException($"Prometheus at {_http.BaseAddress} answered something that is not JSON.");
        }
    }
}

/// <summary>A query's value (null for an empty result), and whether Prometheus answered the query at all.</summary>
internal readonly record struct Answer(bool Answered, double? Value);
