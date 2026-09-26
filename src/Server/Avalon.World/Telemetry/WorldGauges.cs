// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using System.Diagnostics.Metrics;
using Avalon.World.Public;
using Avalon.World.Public.Instances;

namespace Avalon.World.Telemetry;

/// <summary>
/// The world's population gauges. Read on the metrics exporter's thread: the connection list is an
/// immutable snapshot, and the instance count comes from the registry's concurrent dictionary. The
/// registry is null until World.LoadAsync has run.
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
    }
}
