using System.Diagnostics.Metrics;
using Avalon.Common.ValueObjects;
using Avalon.World.Instances;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.World;

/// <summary>
/// #639: every instance is ticked, and timed, on its own. One that throws is logged, rate-limited per instance,
/// and cannot stop the others, where it used to end the world update and, with it, that tick's flushes.
/// </summary>
public class InstanceTickerShould : IDisposable
{
    private static readonly TimeSpan s_tick = TimeSpan.FromSeconds(1d / 60d);

    private readonly Meter _meter = new($"{nameof(InstanceTickerShould)}.{Guid.NewGuid()}");
    private readonly MeterListener _listener = new();
    private readonly List<(string Name, double Value, string? MapType)> _measurements = [];
    private readonly RecordingLogger _logger = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero));
    private readonly InstanceTicker _sut;

    public InstanceTickerShould()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (ReferenceEquals(instrument.Meter, _meter))
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.Start();

        _sut = new InstanceTicker(_logger, _meter, _clock);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _meter.Dispose();
    }

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        string? mapType = null;
        foreach (KeyValuePair<string, object?> tag in tags)
        {
            if (tag.Key == "map.type")
                mapType = tag.Value as string;
        }

        _measurements.Add((instrument.Name, value, mapType));
    }

    private static IMapInstance Instance(MapType type = MapType.Normal, Exception? throws = null)
    {
        IMapInstance instance = Substitute.For<IMapInstance>();
        instance.InstanceId.Returns(Guid.NewGuid());
        instance.TemplateId.Returns(new MapTemplateId(2));
        instance.MapType.Returns(type);
        if (throws is not null)
            instance.When(i => i.Update(Arg.Any<TimeSpan>())).Do(_ => throw throws);
        return instance;
    }

    [Fact]
    public void Tick_every_instance_once_with_the_delta()
    {
        IMapInstance town = Instance(MapType.Town);
        IMapInstance dungeon = Instance();

        _sut.Tick([town, dungeon], s_tick);

        town.Received(1).Update(s_tick);
        dungeon.Received(1).Update(s_tick);
    }

    [Fact]
    public void Tick_the_instances_after_one_that_throws()
    {
        IMapInstance before = Instance();
        IMapInstance broken = Instance(throws: new InvalidOperationException("boom"));
        IMapInstance after = Instance();

        _sut.Tick([before, broken, after], s_tick);

        before.Received(1).Update(s_tick);
        after.Received(1).Update(s_tick);
        (LogLevel level, Exception? exception, _) = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Error, level);
        Assert.IsType<InvalidOperationException>(exception);
    }

    /// <summary>The instance that threw stays live: it is ticked again, and logged again once the interval has passed.</summary>
    [Fact]
    public void Log_an_instance_that_keeps_throwing_at_most_once_per_interval_with_the_count_left_out()
    {
        IMapInstance broken = Instance(throws: new InvalidOperationException("boom"));

        _sut.Tick([broken], s_tick);
        for (int i = 0; i < 59; i++)
        {
            _clock.Advance(s_tick);
            _sut.Tick([broken], s_tick);
        }

        Assert.Single(_logger.Entries);

        _clock.Advance(InstanceTicker.FailureLogInterval);
        _sut.Tick([broken], s_tick);

        Assert.Equal(2, _logger.Entries.Count);
        Assert.Contains("59 earlier throws", _logger.Entries[1].Message, StringComparison.Ordinal);
        broken.Received(61).Update(s_tick);
    }

    /// <summary>The rate limit is per instance: a second broken instance is logged at once.</summary>
    [Fact]
    public void Log_each_broken_instance_on_its_own()
    {
        _sut.Tick([Instance(throws: new InvalidOperationException("a")), Instance(throws: new InvalidOperationException("b"))], s_tick);

        Assert.Equal(2, _logger.Entries.Count);
    }

    [Fact]
    public void Time_each_instance_tagged_with_its_map_type_and_count_its_failures()
    {
        _sut.Tick([Instance(MapType.Town), Instance(), Instance(throws: new InvalidOperationException("boom"))], s_tick);

        var durations =
            _measurements.Where(m => m.Name == "world.instance.update.duration").ToList();
        Assert.Equal(["Town", "Normal", "Normal"], durations.Select(d => d.MapType));
        Assert.All(durations, d => Assert.True(d.Value >= 0));

        (_, double failures, string? failedType) = Assert.Single(_measurements, m => m.Name == "world.instance.update.failures");
        Assert.Equal((1d, "Normal"), (failures, failedType));
    }

    [Fact]
    public void Tick_nothing_and_record_nothing_with_no_instances()
    {
        _sut.Tick([], s_tick);

        Assert.Empty(_measurements);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public void Bucket_instance_update_durations_past_one_frame_up_to_a_one_second_stall()
    {
        using var meter = new Meter($"test-{Guid.NewGuid()}");
        Histogram<double>? published = null;
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, _) =>
            {
                if (instrument.Meter == meter && instrument.Name == "world.instance.update.duration")
                    published = (Histogram<double>)instrument;
            },
        };
        listener.Start();

        _ = new InstanceTicker(NullLogger.Instance, meter);

        IReadOnlyList<double>? buckets = published?.Advice?.HistogramBucketBoundaries;
        Assert.NotNull(buckets);
        Assert.Contains(16667d, buckets);          // one frame at 60 Hz is a bucket edge
        Assert.Contains(25000d, buckets);          // a 20 ms tick lands in 16667-25000, not in +Inf
        Assert.Equal(1_000_000d, buckets[^1]);     // a 1 s stall is still below +Inf
    }

    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, Exception? Exception, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, exception, formatter(state, exception)));
    }
}
