using System;
using System.Reflection;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Character;
using Avalon.World;
using Avalon.World.Public;
using BenchmarkDotNet.Attributes;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// Measures the CPU improvement from GC-010: replacing legacy Activator.CreateInstance
/// + PropertyInfo.SetValue × 2 on every packet dispatch with a single pre-built
/// Func&lt;IConnection, Packet?, object&gt; delegate invocation. Note: [MemoryDiagnoser]
/// shows 1 allocation on both paths because WorldPacketContext&lt;T&gt; (struct) is boxed
/// in both cases—the win is CPU speed, not allocation count. null! inputs isolate
/// construction overhead from business logic.
/// </summary>
[MemoryDiagnoser]
public class GetContextPacketGcBenchmarks
{
    private static readonly Type s_contextType =
        typeof(WorldPacketContext<>).MakeGenericType(typeof(CCharacterListPacket));

    private static readonly PropertyInfo s_packetProp =
        s_contextType.GetProperty(nameof(WorldPacketContext<object>.Packet))
        ?? throw new InvalidOperationException("Property 'Packet' not found on WorldPacketContext<>");

    private static readonly PropertyInfo s_connectionProp =
        s_contextType.GetProperty(nameof(WorldPacketContext<object>.Connection))
        ?? throw new InvalidOperationException("Property 'Connection' not found on WorldPacketContext<>");

    private static readonly Func<IConnection, Packet?, object> s_factory =
        static (conn, pkt) => new WorldPacketContext<CCharacterListPacket>
        {
            Connection = (IWorldConnection)conn!,
            Packet = (CCharacterListPacket)pkt!
        };

    [Benchmark(Baseline = true)]
    public object Legacy_ActivatorAndSetValue()
    {
        object context = Activator.CreateInstance(s_contextType)!;
        s_packetProp.SetValue(context, null);
        s_connectionProp.SetValue(context, null);
        return context;
    }

    [Benchmark]
    public object Delegate_Cached()
        => s_factory(null!, null);
}
