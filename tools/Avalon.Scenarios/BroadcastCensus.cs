using System.Diagnostics.Tracing;
using Avalon.World.Testing.Scenarios;

/// <summary>
/// How a scenario's instances spread their state broadcasts over the ticks: a fresh world of the scenario ticked
/// <c>ticks</c> times with the world's own tick trace (<c>Avalon-World-Tick</c>) attached, counting per tick the
/// instances whose record says it broadcast. Instances in lockstep all broadcast on the same ticks; spread, each tick
/// carries a few. Never timed: the trace costs the tick its timestamps and this listener boxes every payload.
/// </summary>
internal static class BroadcastCensus
{
    public static Result Run(IScenario scenario, int ticks)
    {
        using ScenarioWorld world = scenario.Build();
        using var listener = new Listener(ticks);
        for (int t = 0; t < ticks; t++)
            world.Tick();

        return listener.Result(scenario.Name, world.Instances);
    }

    /// <summary>One scenario's census: per tick, the instances that broadcast.</summary>
    internal sealed record Result(string Scenario, int Instances, int[] PerTick)
    {
        public int Ticks => PerTick.Length;
        public int TicksWithBroadcast => PerTick.Count(c => c > 0);
        public int Min => TicksWithBroadcast == 0 ? 0 : PerTick.Where(c => c > 0).Min();
        public double Mean => TicksWithBroadcast == 0 ? 0 : PerTick.Where(c => c > 0).Average();
        public int Max => PerTick.Length == 0 ? 0 : PerTick.Max();
        public int TicksWithEvery => Instances == 0 ? 0 : PerTick.Count(c => c == Instances);
    }

    /// <summary>
    /// Counts InstanceTick records with broadcast set, closing a tick at its InstanceBatch record. The trace is written
    /// on the ticking thread, and an EventListener is called on the writing thread, so no lock is needed.
    /// </summary>
    private sealed class Listener(int ticks) : EventListener
    {
        private const string Source = "Avalon-World-Tick";
        private readonly List<int> _perTick = new(ticks);
        private int _current;

        public Result Result(string scenario, int instances) => new(scenario, instances, [.. _perTick]);

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == Source)
                EnableEvents(eventSource, EventLevel.Informational);
        }

        protected override void OnEventWritten(EventWrittenEventArgs data)
        {
            if (data.EventSource.Name != Source || data.Payload is null || data.PayloadNames is null)
                return;

            if (data.EventName == "InstanceTick")
            {
                int broadcast = data.PayloadNames.IndexOf("broadcast");
                if (broadcast >= 0 && (long)data.Payload[broadcast]! != 0)
                    _current++;
            }
            else if (data.EventName == "InstanceBatch")
            {
                _perTick.Add(_current);
                _current = 0;
            }
        }
    }
}
