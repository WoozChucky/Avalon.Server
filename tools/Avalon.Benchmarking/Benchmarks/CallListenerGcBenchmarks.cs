using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalon.Common.Cryptography;
using Avalon.Common.ValueObjects;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
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
/// The boxed <see cref="AuthPacketContext{T}"/> struct is the one unavoidable allocation in
/// both paths — identical to the legacy dispatch — and is excluded from the delta.
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
            Connection = new StubAuthConnection(),
            Packet = new CClientInfoPacket()
        };

    private readonly CancellationToken _token = CancellationToken.None;

    [Benchmark(Baseline = true)]
    public Task Legacy_ReflectionInvoke()
        => (Task)s_executeMethod.Invoke(s_handler, new object[] { s_context, _token })!;

    [Benchmark]
    public Task Interface_Dispatch()
        => s_handler.ExecuteAsync(s_context, _token);

    private sealed class StubAuthConnection : IAuthConnection
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Task? ExecuteTask => null;
        public string RemoteEndPoint => string.Empty;
        public IAvalonCryptoSession CryptoSession => null!;
        public ICryptoManager ServerCrypto => null!;
        public AccountId? AccountId { get; set; }
        public AuthServer Server => null!;
        public void Close(bool expected = true) { }
        public void Send(NetworkPacket packet) { }
        public Task StartAsync(CancellationToken token = default) => Task.CompletedTask;
        public byte[] GenerateHandshakeData() => [];
        public bool VerifyHandshakeData(byte[] handshakeData) => false;
    }
}
