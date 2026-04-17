using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Handshake;
using Avalon.Server.Auth;
using Avalon.Server.Auth.Handlers;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// Compares <see cref="MethodInfo.Invoke"/> dispatch (legacy <c>CallListener</c>) against
/// direct <see cref="IPacketHandlerNew.ExecuteAsync"/> virtual dispatch (GC-011 fix).
/// Both paths call the same <see cref="CClientInfoHandler"/>; allocation difference is the
/// eliminated <c>new object[2]</c> args array and boxed <see cref="CancellationToken"/>.
/// </summary>
[MemoryDiagnoser]
public class CallListenerGcBenchmarks
{
    private static readonly MethodInfo s_executeMethod =
        typeof(CClientInfoHandler).GetMethod("ExecuteAsync")
        ?? throw new InvalidOperationException("ExecuteAsync not found on CClientInfoHandler.");

    private static readonly IPacketHandlerNew s_handler =
        new CClientInfoHandler(NullLoggerFactory.Instance);

    private static readonly object s_context =
        new AuthPacketContext<CClientInfoPacket>
        {
            Connection = null!,
            Packet = new CClientInfoPacket()
        };

    private readonly CancellationToken _token = CancellationToken.None;

    [Benchmark(Baseline = true)]
    public Task Legacy_ReflectionInvoke()
        => (Task)s_executeMethod.Invoke(s_handler, new object[] { s_context, _token })!;

    [Benchmark]
    public Task Interface_Dispatch()
        => s_handler.ExecuteAsync(s_context, _token);
}
