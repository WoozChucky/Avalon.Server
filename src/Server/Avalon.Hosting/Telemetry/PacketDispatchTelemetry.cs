// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using System;
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
        Activity? activity = _noSpan.Contains(type)
            ? null
            : _source.StartActivity($"packet {type}", ActivityKind.Server);

        if (activity is not null)
        {
            activity.SetTag("avalon.packet.type", type.ToString());
            activity.SetTag("avalon.connection.id", tags.ConnectionId.ToString());
            activity.SetTag("client.address", tags.ClientAddress);
            if (tags.AccountId is { } account)
                activity.SetTag("avalon.account.id", account);
            if (tags.CharacterId is { } character)
                activity.SetTag("avalon.character.id", character);
        }

        List<KeyValuePair<string, object?>> scope =
        [
            new("PacketType", type.ToString()),
            new("ConnectionId", tags.ConnectionId),
        ];
        if (tags.AccountId is { } accountId)
            scope.Add(new("AccountId", accountId));
        if (tags.CharacterId is { } characterId)
            scope.Add(new("CharacterId", characterId));

        return new PacketDispatch(this, type, activity, logger.BeginScope(scope));
    }

    internal void Record(NetworkPacketType type, double milliseconds, bool failed)
    {
        KeyValuePair<string, object?> packetType = new("avalon.packet.type", type.ToString());
        _duration.Record(milliseconds, packetType, new("avalon.outcome", failed ? "error" : "ok"));
        if (failed)
            _errors.Add(1, packetType);
    }
}
