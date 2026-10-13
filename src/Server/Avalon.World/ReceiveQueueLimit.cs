using System.Diagnostics.Metrics;
using Avalon.Configuration;

namespace Avalon.World;

/// <summary>
/// The bound on what one connection may have waiting for the tick (#927): <c>Network:MaxReceiveQueueDepth</c>, read
/// once at startup, and the count of the connections closed at it, on the meter the host hands in (the world's:
/// <c>world-server</c>). A connection takes the depth when it is created and keeps it.
/// </summary>
public sealed class ReceiveQueueLimit
{
    private readonly Counter<long> _kicks;

    public ReceiveQueueLimit(int maxDepth, Meter meter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDepth, 1);
        MaxDepth = maxDepth;
        _kicks = meter.CreateCounter<long>("network.in.backlog_kicks", "{connections}",
            "Connections closed with Network:MaxReceiveQueueDepth packets received and waiting for the tick");
    }

    /// <summary>The default depth, counting into a meter nothing listens to.</summary>
    public static ReceiveQueueLimit Default { get; } =
        new(new NetworkConfiguration().MaxReceiveQueueDepth, new Meter("avalon-network-disabled"));

    public int MaxDepth { get; }

    internal void Kicked() => _kicks.Add(1);
}
