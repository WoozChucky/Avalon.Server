using System.Diagnostics.Metrics;
using Avalon.Common.Telemetry;
using Avalon.Configuration;
using Avalon.Infrastructure.WorldPlaces;
using Microsoft.Extensions.Options;

namespace Avalon.Api.Identity.Services;

/// <summary>
/// Samples every configured world's places and line every 15 s (#967) for the gauges, and so sweeps what expired even
/// where nobody joins or checks in: the outcomes of what it removed are counted then. The gauges are on a meter of the
/// API's name (<see cref="DiagnosticsConfig.Api.ServiceName"/>, which the exporter collects) that this service owns, so
/// they go with it.
/// </summary>
public sealed class WorldPlaceGauges : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    private readonly IWorldPlaceStore _places;
    private readonly GameWorldAvailability _worlds;
    private readonly IOptions<GameWorkloadConfiguration> _workloads;
    private readonly WorldPlaceMetrics _metrics;
    private readonly TimeProvider _clock;
    private readonly ILogger<WorldPlaceGauges> _logger;
    private readonly Meter _meter = new(DiagnosticsConfig.Api.ServiceName);
    private volatile Sample[] _samples = [];

    private readonly record struct Sample(ushort WorldId, WorldPlaceCounts Counts, int? Limit);

    public WorldPlaceGauges(IWorldPlaceStore places, GameWorldAvailability worlds, IOptions<GameWorkloadConfiguration> workloads,
        WorldPlaceMetrics metrics, TimeProvider clock, ILogger<WorldPlaceGauges> logger)
    {
        (_places, _worlds, _workloads, _metrics, _clock, _logger) = (places, worlds, workloads, metrics, clock, logger);
        _meter.CreateObservableGauge("avalon.world.places.taken", () => Measure(s => s.Counts.Taken), "{accounts}",
            "Places taken per world: every admitted account, in the world, at character select, holding a ticket or held");
        _meter.CreateObservableGauge("avalon.world.places.held", () => Measure(s => s.Counts.Held), "{accounts}",
            "Places held after a dropped connection");
        _meter.CreateObservableGauge("avalon.world.queue.length", () => Measure(s => s.Counts.Queued), "{accounts}",
            "Accounts waiting for a place");
        _meter.CreateObservableGauge("avalon.world.places.limit",
            () => _samples.Where(s => s.Limit is not null).Select(s => Tagged(s.Limit!.Value, s.WorldId)).ToArray(), "{accounts}",
            "The player limit of each world that has one");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, _clock);
        do
        {
            var samples = new List<Sample>();
            foreach (ushort worldId in _workloads.Value.Servers.Select(s => s.WorldId).Distinct())
            {
                try
                {
                    WorldPlaceCounts counts = await _places.CountAsync(worldId, stoppingToken);
                    _metrics.Record(worldId, counts.Sweep);
                    samples.Add(new Sample(worldId, counts, (await _worlds.ReadAsync(worldId, stoppingToken)).PlayerLimit));
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    // A gauge goes without one sample; the next tries again. Redis down is logged by the session paths.
                    _logger.LogDebug("Sampling the places of world {WorldId} failed ({ExceptionType})", worldId, error.GetType().Name);
                }
            }

            _samples = [.. samples];
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public override void Dispose()
    {
        _meter.Dispose();
        base.Dispose();
    }

    // Allocates on the exporter's collection, never on a request.
    private Measurement<int>[] Measure(Func<Sample, int> value) => _samples.Select(s => Tagged(value(s), s.WorldId)).ToArray();

    private static Measurement<int> Tagged(int value, ushort worldId) =>
        new(value, new KeyValuePair<string, object?>("world", (int)worldId));
}
