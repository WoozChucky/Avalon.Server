using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Movement;
using Avalon.World.Testing.Fakes;
using BenchmarkDotNet.Attributes;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>Production arrival, session/map filtering and dispatch; payload creation is outside measurement.</summary>
[MemoryDiagnoser]
public class ReceiveQueueBenchmarks
{
    private const int Cycles = 256;
    private ReceiveQueueFixture _fixture = null!;
    private readonly CPlayerInputPacket _input = new();

    [Params(1, 32, 150)]
    public int PacketsPerTick { get; set; }

    [GlobalSetup]
    public void Setup() => _fixture = new ReceiveQueueFixture();

    [Benchmark(OperationsPerInvoke = Cycles)]
    public void ReceiveAndDispatch()
    {
        for (int cycle = 0; cycle < Cycles; cycle++)
        {
            for (int packet = 0; packet < PacketsPerTick; packet++)
                _fixture.Connection.Deliver(NetworkPacketType.CMSG_PLAYER_INPUT, _input);
            _fixture.Pump();
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _fixture.Dispose();
}
