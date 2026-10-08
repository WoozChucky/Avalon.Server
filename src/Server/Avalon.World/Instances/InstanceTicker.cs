using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Telemetry;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Instances;

/// <summary>
/// Ticks every live instance, each on its own (#639). An instance whose update throws is logged and the
/// others still tick, so one broken instance cannot take the rest of the world update, and the flushes the
/// tick loop runs after it, down with it. The instance stays live and is ticked again next tick: nothing
/// here decides that a throw means it is broken for good.
/// </summary>
/// <remarks>
/// Each update is timed into <c>world.instance.update.duration</c> (microseconds, tagged with the map type),
/// and each throw counted in <c>world.instance.update.failures</c>, so the cost of one busy instance (a
/// crowded town, say) can be told apart from the cost of many: the measurement #639 needs before ticking
/// instances in parallel. An instance that throws every tick is logged at most once per
/// <see cref="FailureLogInterval" />, with how many throws were left out since, so it cannot flood the log at
/// 60 Hz. Tick thread only.
/// </remarks>
public sealed class InstanceTicker
{
    /// <summary>The least time between two logged throws of one instance.</summary>
    public static readonly TimeSpan FailureLogInterval = TimeSpan.FromSeconds(10);

    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly Histogram<double> _duration;
    private readonly Counter<long> _failures;

    // Weak, so an instance disposed and dropped by the registry takes its entry with it.
    private readonly ConditionalWeakTable<IMapInstance, FailureLog> _failureLogs = new();

    public InstanceTicker(ILogger logger, Meter meter, TimeProvider? time = null)
    {
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _duration = WorldHistograms.Microseconds(meter, "world.instance.update.duration",
            "Duration of one instance's update in microseconds, by map type");
        _failures = meter.CreateCounter<long>("world.instance.update.failures", "{failures}",
            "Instance updates that threw, by map type");
    }

    public void Tick(IReadOnlyCollection<IMapInstance> instances, TimeSpan deltaTime)
    {
        foreach (IMapInstance instance in instances)
        {
            long start = Stopwatch.GetTimestamp();
            try
            {
                instance.Update(deltaTime);
            }
            catch (Exception e)
            {
                Failed(instance, e);
            }

            _duration.Record(Stopwatch.GetElapsedTime(start).TotalMicroseconds, MapTypeTag(instance.MapType));
        }
    }

    private void Failed(IMapInstance instance, Exception e)
    {
        _failures.Add(1, MapTypeTag(instance.MapType));

        FailureLog log = _failureLogs.GetOrCreateValue(instance);
        DateTimeOffset now = _time.GetUtcNow();
        if (log.LastLogged is { } last && now - last < FailureLogInterval)
        {
            log.Suppressed++;
            return;
        }

        _logger.LogError(e,
            "Instance {InstanceId} (map {TemplateId}, {MapType}) threw during its update; the other instances still ticked. {Suppressed} earlier throws of it were not logged",
            instance.InstanceId, instance.TemplateId.Value, instance.MapType, log.Suppressed);
        log.LastLogged = now;
        log.Suppressed = 0;
    }

    private static readonly KeyValuePair<string, object?> s_townTag = new("map.type", nameof(MapType.Town));
    private static readonly KeyValuePair<string, object?> s_normalTag = new("map.type", nameof(MapType.Normal));

    /// <summary>The tag for a map type, cached for the two there are, so timing an update allocates nothing.</summary>
    private static KeyValuePair<string, object?> MapTypeTag(MapType type) => type switch
    {
        MapType.Town => s_townTag,
        MapType.Normal => s_normalTag,
        _ => new KeyValuePair<string, object?>("map.type", type.ToString()),
    };

    private sealed class FailureLog
    {
        public DateTimeOffset? LastLogged { get; set; }
        public int Suppressed { get; set; }
    }
}
