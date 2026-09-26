using System.Diagnostics.Metrics;
using Avalon.World.Public;
using Avalon.World.Public.Instances;
using Avalon.World.Telemetry;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Telemetry;

public sealed class WorldGaugesShould : IDisposable
{
    private readonly Meter _meter = new($"test-{Guid.NewGuid()}");

    public void Dispose() => _meter.Dispose();

    private Dictionary<string, int> Observe()
    {
        Dictionary<string, int> observed = [];
        using MeterListener listener = new();
        listener.InstrumentPublished = (instrument, l) => { if (instrument.Meter == _meter) l.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<int>((instrument, value, _, _) => observed[instrument.Name] = value);
        listener.Start();
        listener.RecordObservableInstruments();
        return observed;
    }

    private static IWorldConnection Connection(bool inGame)
    {
        IWorldConnection connection = Substitute.For<IWorldConnection>();
        connection.InGame.Returns(inGame);
        return connection;
    }

    [Fact]
    public void Count_only_the_connections_with_a_character_in_the_world()
    {
        IWorldConnection[] connections = [Connection(true), Connection(false), Connection(true)];
        WorldGauges.Register(_meter, () => connections, () => null);

        Assert.Equal(2, Observe()["avalon.world.players.online"]);
    }

    [Fact]
    public void Count_the_active_instances()
    {
        IInstanceRegistry registry = Substitute.For<IInstanceRegistry>();
        registry.ActiveInstances.Returns([Substitute.For<IMapInstance>(), Substitute.For<IMapInstance>()]);
        WorldGauges.Register(_meter, () => [], () => registry);

        Assert.Equal(2, Observe()["avalon.world.instances.active"]);
    }

    [Fact]
    public void Report_no_instances_before_the_world_has_loaded()
    {
        WorldGauges.Register(_meter, () => [], () => null);

        Assert.Equal(0, Observe()["avalon.world.instances.active"]);
    }
}
