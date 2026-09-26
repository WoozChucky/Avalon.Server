using System.Diagnostics.Metrics;
using Avalon.Hosting.Networking;
using Xunit;

namespace Avalon.Server.Auth.UnitTests.Networking;

/// <summary>
/// A Meter keeps every observable instrument, and the closure it captured, until the Meter is
/// disposed. Registering rate gauges per connection therefore kept every connection alive for
/// the life of the process; in Kubernetes the TCP probes alone open one every few seconds.
/// </summary>
public class ConnectionRateRegistryShould
{
    private sealed class Rates(double packetsOut) : IConnectionRates
    {
        public Guid Id { get; } = Guid.NewGuid();
        public double PacketSentRate { get; } = packetsOut;
        public double PacketReceivedRate => 0;
        public double BytesSentRate => 0;
        public double BytesReceivedRate => 0;
    }

    private static (Meter meter, ConnectionRateRegistry registry) Create() {
        Meter meter = new($"test-{Guid.NewGuid()}");
        return (meter, new ConnectionRateRegistry(meter));
    }

    [Fact]
    public void Publish_one_instrument_per_rate_however_many_connections_are_tracked()
    {
        (Meter meter, ConnectionRateRegistry registry) = Create();
        using (meter)
        {
            for (int i = 0; i < 50; i++) registry.Track(new Rates(1));

            int published = 0;
            using MeterListener listener = new();
            listener.InstrumentPublished = (instrument, _) => { if (instrument.Meter == meter) published++; };
            listener.Start();

            Assert.Equal(4, published);
        }
    }

    [Fact]
    public void Report_the_sum_of_the_connections_still_tracked()
    {
        (Meter meter, ConnectionRateRegistry registry) = Create();
        using (meter)
        {
            Rates kept = new(3), closed = new(5);
            registry.Track(kept);
            registry.Track(closed);
            registry.Untrack(closed.Id);

            double observed = double.NaN;
            using MeterListener listener = new();
            listener.InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter == meter && instrument.Name == "network.out.packets.rate") l.EnableMeasurementEvents(instrument);
            };
            listener.SetMeasurementEventCallback<double>((_, value, _, _) => observed = value);
            listener.Start();
            listener.RecordObservableInstruments();

            Assert.Equal(3, observed);
            Assert.Equal(1, registry.Count);
        }
    }
}
