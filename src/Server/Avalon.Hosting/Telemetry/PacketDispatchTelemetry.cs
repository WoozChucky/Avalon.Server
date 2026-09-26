// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Net;
using Avalon.Configuration;
using Avalon.Network.Packets.Abstractions;
using Microsoft.Extensions.Logging;

namespace Avalon.Hosting.Telemetry;

/// <summary>Who a packet came from, as its span, metrics and log scope describe it.</summary>
public readonly record struct PacketTags(Guid ConnectionId, string ClientAddress, long? AccountId, uint? CharacterId)
{
    /// <summary>The address without its port: "203.0.113.7:5000" is "203.0.113.7".</summary>
    public static string AddressOf(string remoteEndPoint) =>
        IPEndPoint.TryParse(remoteEndPoint, out IPEndPoint? endPoint) ? endPoint.Address.ToString() : remoteEndPoint;
}

/// <summary>
/// What every packet handler run records, at both dispatch points: a span (unless its type is too
/// frequent to trace), its duration and outcome, an error count, and a log scope carrying who sent
/// it, so the handler's own log lines can be found by account, character or connection.
/// </summary>
public sealed class PacketDispatchTelemetry
{
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
            "Time a packet handler took");
        _errors = meter.CreateCounter<long>("avalon.packet.handler.errors", "{errors}",
            "Packet handlers that threw");
    }

    /// <exception cref="ArgumentException">A configured name is not a NetworkPacketType.</exception>
    public static PacketDispatchTelemetry From(ActivitySource source, Meter meter, TelemetryConfiguration config) =>
        new(source, meter, config.NoSpanPacketTypes?.Select(Parse).ToArray());

    private static NetworkPacketType Parse(string name) =>
        Enum.TryParse(name, ignoreCase: false, out NetworkPacketType type) && Enum.IsDefined(type)
            ? type
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

/// <summary>One handler run. Dispose it when the handler returns; call <see cref="Fail" /> if it threw.</summary>
public sealed class PacketDispatch : IDisposable
{
    private readonly PacketDispatchTelemetry _owner;
    private readonly NetworkPacketType _type;
    private readonly Activity? _activity;
    private readonly IDisposable? _scope;
    private readonly long _started = Stopwatch.GetTimestamp();
    private bool _failed;
    private bool _disposed;

    internal PacketDispatch(PacketDispatchTelemetry owner, NetworkPacketType type, Activity? activity, IDisposable? scope)
    {
        _owner = owner;
        _type = type;
        _activity = activity;
        _scope = scope;
    }

    public void Fail(Exception exception)
    {
        _failed = true;
        _activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        _activity?.AddException(exception);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _owner.Record(_type, Stopwatch.GetElapsedTime(_started).TotalMilliseconds, _failed);
        _activity?.SetTag("avalon.outcome", _failed ? "error" : "ok");
        _activity?.Dispose();
        _scope?.Dispose();
    }
}
