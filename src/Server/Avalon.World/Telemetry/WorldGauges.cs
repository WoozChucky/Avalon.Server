using System.Diagnostics.Metrics;
using Avalon.World.Public;
using Avalon.World.Public.Instances;

namespace Avalon.World.Telemetry;

/// <summary>
/// The world's population and receive-queue gauges. Read on the metrics exporter's thread, never on the tick:
/// the connection list is an immutable snapshot, each connection's receive queue is a concurrent queue, and
/// the instance count comes from the registry's concurrent dictionary. The registry is null until
/// World.LoadAsync has run.
/// </summary>
public static class WorldGauges
{
    public static void Register(Meter meter, Func<IEnumerable<IWorldConnection>> connections,
        Func<IInstanceRegistry?> instances)
    {
        meter.CreateObservableGauge("avalon.world.players.online", () => connections().Count(c => c.InGame),
            "{players}", "Characters in the world");
        meter.CreateObservableGauge("avalon.world.instances.active", () => instances()?.ActiveInstances.Count ?? 0,
            "{instances}", "Map instances alive");
        meter.CreateObservableGauge("world.receive_queue.depth", () => ReceiveQueueDepth(connections()),
            "{packets}", "Packets received and not yet dispatched: the total over connections, and the deepest one");
    }

    // Allocates on the exporter's collection, never on the tick.
    private static Measurement<long>[] ReceiveQueueDepth(IEnumerable<IWorldConnection> connections)
    {
        long total = 0, max = 0;
        foreach (IWorldConnection connection in connections)
        {
            if (connection is not WorldConnection world) continue;
            int depth = world.ReceiveQueueDepth;
            total += depth;
            if (depth > max) max = depth;
        }

        return
        [
            new Measurement<long>(total, new KeyValuePair<string, object?>("stat", "total")),
            new Measurement<long>(max, new KeyValuePair<string, object?>("stat", "max")),
        ];
    }
}
