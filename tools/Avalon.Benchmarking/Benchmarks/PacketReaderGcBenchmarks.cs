using System.IO;
using System.Linq;
using System.Reflection;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Character;
using BenchmarkDotNet.Attributes;
using ProtoBuf;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// Measures the allocation reduction from GC-007: replacing MethodInfo.Invoke
/// (new object?[3] args array + boxed ReadOnlyMemory struct) with a cached typed
/// Func&lt;ReadOnlyMemory&lt;byte&gt;, Packet?&gt; delegate in PacketReader.Read().
/// </summary>
[MemoryDiagnoser]
public class PacketReaderGcBenchmarks
{
    private static readonly MethodInfo s_legacyMethodInfo =
        (typeof(Serializer).GetMethods()
            .FirstOrDefault(m => m is { Name: "Deserialize", IsGenericMethod: true }
                                 && m.GetParameters().Length == 3
                                 && m.GetParameters()[0].ParameterType == typeof(ReadOnlyMemory<byte>))
         ?? throw new InvalidOperationException(
             "Serializer.Deserialize<T>(ReadOnlyMemory<byte>, ...) overload not found"))
        .MakeGenericMethod(typeof(CCharacterListPacket));

    private static readonly Func<ReadOnlyMemory<byte>, Packet?> s_delegate =
        static mem => Serializer.Deserialize<CCharacterListPacket>(mem) as Packet;

    private byte[] _payload = null!;

    [GlobalSetup]
    public void Setup()
    {
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, new CCharacterListPacket());
        _payload = ms.ToArray();
    }

    [Benchmark(Baseline = true)]
    public Packet? Legacy_ReflectionInvoke()
    {
        ReadOnlyMemory<byte> mem = new(_payload);
        return s_legacyMethodInfo.Invoke(null, new object?[] { mem, null, null }) as Packet;
    }

    [Benchmark]
    public Packet? Delegate_Cached()
        => s_delegate(new ReadOnlyMemory<byte>(_payload));
}
