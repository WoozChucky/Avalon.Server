// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using Avalon.Configuration;
using Avalon.Network.Packets.Abstractions;
using Microsoft.Extensions.Logging;

namespace Avalon.Hosting.Telemetry;

/// <summary>
/// What every packet handler run records, at both dispatch points: a span (unless its type is too
/// frequent to trace), its duration and outcome, an error count, and a log scope carrying who sent
/// it, so the handler's own log lines can be found by account, character or connection.
/// </summary>
public sealed class PacketDispatchTelemetry
{
    /// <summary>
    /// Handler duration buckets, in ms. Most tick handlers run in microseconds; the SDK's default
    /// buckets start at 5 ms and would put them all in the first one.
    /// </summary>
    private static readonly double[] DurationBuckets =
        [0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000];

    public static readonly IReadOnlyList<NetworkPacketType> DefaultNoSpanPacketTypes =
        [NetworkPacketType.CMSG_PLAYER_INPUT, NetworkPacketType.CMSG_PONG];

    /// <summary>Records into a source and a meter nothing listens to.</summary>
    public static readonly PacketDispatchTelemetry Disabled =
        new(new ActivitySource("avalon-telemetry-disabled"), new Meter("avalon-telemetry-disabled"));

    // Enum.ToString allocates on every call; the names are tagged on every packet.
    private static readonly FrozenDictionary<NetworkPacketType, string> Names =
        Enum.GetValues<NetworkPacketType>().Distinct().ToFrozenDictionary(t => t, t => t.ToString());

    internal static string NameOf(NetworkPacketType type) => Names.TryGetValue(type, out string? name) ? name : type.ToString();

    private readonly ActivitySource _source;
    private readonly HashSet<NetworkPacketType> _noSpan;
    private readonly Histogram<double> _duration;
    private readonly Counter<long> _errors;

    public PacketDispatchTelemetry(ActivitySource source, Meter meter, IEnumerable<NetworkPacketType>? noSpanPacketTypes = null)
    {
        _source = source;
        _noSpan = [.. noSpanPacketTypes ?? DefaultNoSpanPacketTypes];
        _duration = meter.CreateHistogram<double>("avalon.packet.handler.duration", "ms",
            "Time a packet handler took", tags: null,
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = DurationBuckets });
        _errors = meter.CreateCounter<long>("avalon.packet.handler.errors", "{errors}",
            "Packet handlers that threw");
    }

    /// <exception cref="ArgumentException">A configured name is not a NetworkPacketType.</exception>
    public static PacketDispatchTelemetry From(ActivitySource source, Meter meter, TelemetryConfiguration config) =>
        new(source, meter, config.NoSpanPacketTypes?.Select(Parse).ToArray());

    // Exact names only: Enum.TryParse also takes numbers and comma lists, and a list ORs its values
    // into whatever other packet type the result happens to equal.
    private static NetworkPacketType Parse(string name) =>
        Enum.GetNames<NetworkPacketType>().Contains(name, StringComparer.Ordinal)
            ? Enum.Parse<NetworkPacketType>(name)
            : throw new ArgumentException(
                $"Hosting:Telemetry:NoSpanPacketTypes: '{name}' is not a NetworkPacketType", nameof(name));

    public PacketDispatch Begin(NetworkPacketType type, PacketTags tags, ILogger logger)
    {
        Activity? previous = Activity.Current;
        Activity? activity = null;
        IDisposable? scope = null;
        try
        {
            activity = StartSpan(type, tags);
            scope = logger.BeginScope(new PacketScope(type, tags));
        }
        catch (Exception)
        {
            // Telemetry must never cost the packet. A throwing activity listener has already made its
            // activity Current by the time it throws, so Current is put back, or every later span on
            // this thread would nest under it; the dispatch is still timed, and the handler runs.
            if (activity is null && !ReferenceEquals(Activity.Current, previous))
                Activity.Current = previous;
        }

        return new PacketDispatch(this, type, activity, scope);
    }

    private Activity? StartSpan(NetworkPacketType type, PacketTags tags)
    {
        if (_noSpan.Contains(type))
            return null;

        Activity? activity = _source.StartActivity($"packet {type}", ActivityKind.Server);
        if (activity is null)
            return null;

        activity.SetTag("avalon.packet.type", NameOf(type));
        activity.SetTag("avalon.connection.id", tags.ConnectionId.ToString());
        activity.SetTag("client.address", tags.ClientAddress);
        if (tags.AccountId is { } account)
            activity.SetTag("avalon.account.id", account);
        if (tags.CharacterId is { } character)
            activity.SetTag("avalon.character.id", character);
        return activity;
    }

    internal void Record(NetworkPacketType type, double milliseconds, bool failed)
    {
        KeyValuePair<string, object?> packetType = new("avalon.packet.type", NameOf(type));
        _duration.Record(milliseconds, packetType, new("avalon.outcome", failed ? "error" : "ok"));
        if (failed)
            _errors.Add(1, packetType);
    }
}
