// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Linq;
using Avalon.Common.Telemetry;

namespace Avalon.Hosting.Networking;

/// <summary>The per-connection traffic rates the network gauges report.</summary>
public interface IConnectionRates
{
    Guid Id { get; }
    double PacketSentRate { get; }
    double PacketReceivedRate { get; }
    double BytesSentRate { get; }
    double BytesReceivedRate { get; }
}

/// <summary>
/// The network rate gauges, registered once per Meter and reporting the sum over the connections
/// still open. A Meter keeps every observable instrument, and the closure it captured, until it
/// is disposed, so the gauges each connection used to register kept every connection alive for
/// the life of the process.
/// </summary>
public sealed class ConnectionRateRegistry
{
    public static readonly ConnectionRateRegistry Shared = new(DiagnosticsConfig.World.Meter);

    private readonly ConcurrentDictionary<Guid, IConnectionRates> _live = new();

    public ConnectionRateRegistry(Meter meter)
    {
        meter.CreateObservableGauge("network.out.packets.rate", () => Sum(c => c.PacketSentRate),
            "packets/s", "Rate of packets sent");
        meter.CreateObservableGauge("network.in.packets.rate", () => Sum(c => c.PacketReceivedRate),
            "packets/s", "Rate of packets received");
        meter.CreateObservableGauge("network.out.bytes.rate", () => Sum(c => c.BytesSentRate),
            "bytes/s", "Rate of bytes sent");
        meter.CreateObservableGauge("network.in.bytes.rate", () => Sum(c => c.BytesReceivedRate),
            "bytes/s", "Rate of bytes received");
    }

    public int Count => _live.Count;

    public void Track(IConnectionRates connection) => _live[connection.Id] = connection;

    public void Untrack(Guid id) => _live.TryRemove(id, out _);

    private double Sum(Func<IConnectionRates, double> rate) => _live.Values.Sum(rate);
}
