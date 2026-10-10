namespace Avalon.LoadTest.Ramp;

/// <summary>Whether a step's instances by map type could be read.</summary>
public enum InstancesReadout
{
    /// <summary>The world exports its instance updates by map type and the window held enough samples.</summary>
    Reported,

    /// <summary>The world reports its ticks and no <c>world.instance.update.duration</c> at all: a build without it.</summary>
    NotExported,

    /// <summary>
    /// Nothing to tell: a query failed, Prometheus has nothing from the world at all (a stalled export, a scrape gap, a
    /// wrong world id), or the window held too few samples for a rate, as can happen to any of a step's values.
    /// </summary>
    Unknown,
}

/// <summary>
/// The map instances the world ticked over a step's judged window, by map type: each instance's update is recorded once
/// per tick in <c>world.instance.update.duration</c>, tagged <c>map.type</c>, so the updates per tick of a map type are
/// its mean count of live instances over the window. <c>avalon.world.instances.active</c> carries no map type, so it
/// gives the total only. Reported for reading, never a limit.
/// </summary>
/// <param name="Town">The town instances ticked per tick, on average; null unless <see cref="Readout"/> is reported.</param>
/// <param name="Forest">
/// The normal-map instances ticked per tick, on average: on the load-test world the forest's, the only normal map its
/// portals lead to. Null unless <see cref="Readout"/> is reported.
/// </param>
public sealed record InstancesByMap(InstancesReadout Readout, double? Town, double? Forest)
{
    /// <summary>The <c>map.type</c> tag of a town instance.</summary>
    public const string TownType = "Town";

    /// <summary>The <c>map.type</c> tag of a normal (forest) instance.</summary>
    public const string ForestType = "Normal";

    /// <summary>Nothing read.</summary>
    public static InstancesByMap Unknown { get; } = new(InstancesReadout.Unknown, null, null);

    /// <summary>
    /// The instances from two answers. Whether the world exports the update histogram (<paramref name="exportedAnswered"/>
    /// false when that query failed; <paramref name="exportedSeries"/> its series count, 0 for a world that reports its
    /// ticks and no instance update, null when Prometheus has nothing from the world at all), and the updates per tick by
    /// map type (null when that query failed). A count of 0 is <see cref="InstancesReadout.NotExported"/>. A failed or
    /// empty count, a failed or empty rate, or a rate that is not a finite number (no tick in the window) is
    /// <see cref="InstancesReadout.Unknown"/>, as is a rate without the town's (which ticks as long as the world does).
    /// With the town's rate, a map type the result lacks reads 0: no instance of it has ticked since the world started,
    /// or its first did so only in the window's last sample, too late for a rate.
    /// </summary>
    public static InstancesByMap From(bool exportedAnswered, double? exportedSeries, IReadOnlyDictionary<string, double>? perTick)
    {
        if (!exportedAnswered || exportedSeries is null) return Unknown;
        if (exportedSeries is not > 0) return new(InstancesReadout.NotExported, null, null);
        if (perTick is null || perTick.Count == 0 || perTick.Values.Any(value => !double.IsFinite(value))) return Unknown;
        if (!perTick.ContainsKey(TownType)) return Unknown;

        return new(InstancesReadout.Reported, perTick.GetValueOrDefault(TownType), perTick.GetValueOrDefault(ForestType));
    }
}
