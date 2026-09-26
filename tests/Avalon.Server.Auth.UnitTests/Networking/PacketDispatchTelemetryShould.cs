using System.Diagnostics;
using System.Diagnostics.Metrics;
using Avalon.Configuration;
using Avalon.Hosting.Telemetry;
using Avalon.Network.Packets.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Avalon.Server.Auth.UnitTests.Networking;

/// <summary>
/// The per-packet telemetry both dispatch points share: one span per packet (except the chatty
/// ones), a duration and an error count, and a log scope carrying who sent it.
/// </summary>
public sealed class PacketDispatchTelemetryShould : IDisposable
{
    private readonly string _name = $"test-{Guid.NewGuid()}";
    private readonly ActivitySource _source;
    private readonly Meter _meter;
    private readonly ActivityListener _spanListener;
    private readonly MeterListener _meterListener = new();
    private readonly List<Activity> _spans = [];
    private readonly List<(string Instrument, double Value, Dictionary<string, object?> Tags)> _measurements = [];

    private static readonly PacketTags Tags = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "203.0.113.7", 42, 7);

    public PacketDispatchTelemetryShould()
    {
        _source = new ActivitySource(_name);
        _meter = new Meter(_name);
        _spanListener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == _name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => _spans.Add(a),
        };
        ActivitySource.AddActivityListener(_spanListener);

        _meterListener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter == _meter) l.EnableMeasurementEvents(instrument);
        };
        _meterListener.SetMeasurementEventCallback<double>((i, v, t, _) => Record(i, v, t));
        _meterListener.SetMeasurementEventCallback<long>((i, v, t, _) => Record(i, v, t));
        _meterListener.Start();
    }

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        Dictionary<string, object?> copy = [];
        foreach (KeyValuePair<string, object?> tag in tags) copy[tag.Key] = tag.Value;
        _measurements.Add((instrument.Name, value, copy));
    }

    public void Dispose()
    {
        _spanListener.Dispose();
        _meterListener.Dispose();
        _source.Dispose();
        _meter.Dispose();
    }

    private PacketDispatchTelemetry Create(IEnumerable<NetworkPacketType>? noSpan = null) => new(_source, _meter, noSpan);

    [Fact]
    public void Start_one_server_span_per_packet_with_who_sent_it()
    {
        using (Create().Begin(NetworkPacketType.CMSG_AUTH, Tags, new ScopeLogger())) { }

        Activity span = Assert.Single(_spans);
        Assert.Equal("packet CMSG_AUTH", span.DisplayName);
        Assert.Equal(ActivityKind.Server, span.Kind);
        Assert.Equal("CMSG_AUTH", span.GetTagItem("avalon.packet.type"));
        Assert.Equal("11111111-1111-1111-1111-111111111111", span.GetTagItem("avalon.connection.id"));
        Assert.Equal("203.0.113.7", span.GetTagItem("client.address"));
        Assert.Equal(42L, span.GetTagItem("avalon.account.id"));
        Assert.Equal(7u, span.GetTagItem("avalon.character.id"));
        Assert.Equal("ok", span.GetTagItem("avalon.outcome"));
        Assert.Equal(ActivityStatusCode.Unset, span.Status);
    }

    [Fact]
    public void Leave_out_the_account_and_character_when_they_are_not_known()
    {
        using (Create().Begin(NetworkPacketType.CMSG_AUTH, Tags with { AccountId = null, CharacterId = null }, new ScopeLogger())) { }

        Activity span = Assert.Single(_spans);
        Assert.Null(span.GetTagItem("avalon.account.id"));
        Assert.Null(span.GetTagItem("avalon.character.id"));
    }

    [Fact]
    public void Start_no_span_for_the_chatty_packet_types_but_still_time_them()
    {
        PacketDispatchTelemetry telemetry = Create();
        using (telemetry.Begin(NetworkPacketType.CMSG_PLAYER_INPUT, Tags, new ScopeLogger())) { }
        using (telemetry.Begin(NetworkPacketType.CMSG_PONG, Tags, new ScopeLogger())) { }

        Assert.Empty(_spans);
        Assert.Equal(2, _measurements.Count(m => m.Instrument == "avalon.packet.handler.duration"));
    }

    [Fact]
    public void Mark_a_failed_handler_as_an_error_and_count_it()
    {
        using (PacketDispatch dispatch = Create().Begin(NetworkPacketType.CMSG_AUTH, Tags, new ScopeLogger()))
            dispatch.Fail(new InvalidOperationException("boom"));

        Activity span = Assert.Single(_spans);
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal("boom", span.StatusDescription);
        Assert.Equal("error", span.GetTagItem("avalon.outcome"));
        Assert.Contains(span.Events, e => e.Name == "exception");

        var error = Assert.Single(_measurements, m => m.Instrument == "avalon.packet.handler.errors");
        Assert.Equal(1, error.Value);
        Assert.Equal("CMSG_AUTH", error.Tags["avalon.packet.type"]);
    }

    [Fact]
    public void Record_the_duration_by_packet_type_and_outcome()
    {
        PacketDispatchTelemetry telemetry = Create();
        using (telemetry.Begin(NetworkPacketType.CMSG_AUTH, Tags, new ScopeLogger())) { }
        using (PacketDispatch failed = telemetry.Begin(NetworkPacketType.CMSG_AUTH, Tags, new ScopeLogger()))
            failed.Fail(new InvalidOperationException());

        var durations = _measurements.Where(m => m.Instrument == "avalon.packet.handler.duration").ToList();
        Assert.Equal(2, durations.Count);
        Assert.All(durations, d => Assert.Equal("CMSG_AUTH", d.Tags["avalon.packet.type"]));
        Assert.Equal(["ok", "error"], durations.Select(d => d.Tags["avalon.outcome"]));
        Assert.All(durations, d => Assert.True(d.Value >= 0));
    }

    [Fact]
    public void Bucket_handler_durations_finely_enough_for_sub_millisecond_handlers()
    {
        Create();

        Histogram<double>? histogram = null;
        using MeterListener listener = new();
        listener.InstrumentPublished = (instrument, _) =>
        {
            if (instrument.Meter == _meter && instrument.Name == "avalon.packet.handler.duration")
                histogram = instrument as Histogram<double>;
        };
        listener.Start();

        IReadOnlyList<double>? buckets = histogram?.Advice?.HistogramBucketBoundaries;
        Assert.NotNull(buckets);
        Assert.True(buckets[0] <= 0.01, $"first bucket {buckets[0]}ms is too coarse for tick handlers");
        Assert.True(buckets[^1] >= 1000, $"last bucket {buckets[^1]}ms cuts off slow handlers");
    }

    [Fact]
    public void Record_each_dispatch_once_even_if_disposed_twice()
    {
        PacketDispatch dispatch = Create().Begin(NetworkPacketType.CMSG_AUTH, Tags, new ScopeLogger());
        dispatch.Dispose();
        dispatch.Dispose();

        Assert.Single(_measurements, m => m.Instrument == "avalon.packet.handler.duration");
    }

    [Fact]
    public void Open_a_log_scope_with_the_connection_context_for_as_long_as_the_handler_runs()
    {
        ScopeLogger logger = new();

        using (Create().Begin(NetworkPacketType.CMSG_AUTH, Tags, logger))
        {
            var scope = Assert.Single(logger.Scopes);
            var fields = Assert.IsAssignableFrom<IEnumerable<KeyValuePair<string, object?>>>(scope.State).ToDictionary();
            Assert.Equal("CMSG_AUTH", fields["PacketType"]);
            Assert.Equal(Tags.ConnectionId, fields["ConnectionId"]);
            Assert.Equal(42L, fields["AccountId"]);
            Assert.Equal(7u, fields["CharacterId"]);
            Assert.False(scope.Disposed);
        }

        Assert.True(logger.Scopes[0].Disposed);
    }

    [Fact]
    public void Read_the_no_span_list_from_configuration_replacing_the_defaults()
    {
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Hosting:Telemetry:NoSpanPacketTypes:0"] = "CMSG_AUTH",
        }).Build();
        HostingConfiguration hosting = new();
        config.GetSection("Hosting").Bind(hosting);

        PacketDispatchTelemetry telemetry = PacketDispatchTelemetry.From(_source, _meter, hosting.Telemetry);
        using (telemetry.Begin(NetworkPacketType.CMSG_AUTH, Tags, new ScopeLogger())) { }
        using (telemetry.Begin(NetworkPacketType.CMSG_PONG, Tags, new ScopeLogger())) { }

        Activity span = Assert.Single(_spans);
        Assert.Equal("packet CMSG_PONG", span.DisplayName);
    }

    [Fact]
    public void Keep_the_defaults_when_nothing_is_configured()
    {
        PacketDispatchTelemetry telemetry = PacketDispatchTelemetry.From(_source, _meter, new TelemetryConfiguration());
        using (telemetry.Begin(NetworkPacketType.CMSG_PONG, Tags, new ScopeLogger())) { }

        Assert.Empty(_spans);
    }

    [Fact]
    public void Refuse_a_packet_type_name_that_does_not_exist()
    {
        var config = new TelemetryConfiguration { NoSpanPacketTypes = ["CMSG_NOPE"] };

        ArgumentException error = Assert.Throws<ArgumentException>(() => PacketDispatchTelemetry.From(_source, _meter, config));
        Assert.Contains("CMSG_NOPE", error.Message);
    }

    [Theory]
    [InlineData("203.0.113.7:5000", "203.0.113.7")]
    [InlineData("[2001:db8::1]:5000", "2001:db8::1")]
    [InlineData("Unknown", "Unknown")]
    public void Strip_the_port_from_a_remote_end_point(string endPoint, string address)
    {
        Assert.Equal(address, PacketTags.AddressOf(endPoint));
    }

    private sealed class ScopeLogger : ILogger
    {
        public List<Scope> Scopes { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            Scope scope = new(state);
            Scopes.Add(scope);
            return scope;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) { }

        public sealed class Scope(object state) : IDisposable
        {
            public object State { get; } = state;
            public bool Disposed { get; private set; }
            public void Dispose() => Disposed = true;
        }
    }
}
