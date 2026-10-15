using Avalon.Configuration;
using Avalon.Hosting.Networking;
using BenchmarkDotNet.Attributes;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// The read loop's framing alone: <see cref="PacketStream.EnumerateRawFramesAsync(int, int, TimeSpan, TimeProvider, CancellationToken)" /> over frames laid back to back in
/// memory, with the length check every frame gets (<c>Hosting:MaxInboundFrameSize</c>). No socket, TLS, parsing or
/// decoding. An 80-byte frame has a one-byte length prefix and a 300-byte one two; both are sizes a client sends.
/// </summary>
[MemoryDiagnoser]
public class PacketStreamBenchmarks
{
    private const int Frames = 4096;
    private const int ReadBuffer = 4096;
    private const int MaxFrame = 16_384;

    // The deadline for a started frame, as a server's read loop runs it: a 4 KiB read ends mid-frame, so frames here
    // arrive in pieces as they do off a socket, and each such frame starts and stops the clock (#938).
    private static readonly TimeSpan s_frameTimeout = TimeSpan.FromSeconds(HostingConfiguration.DefaultInboundFrameTimeoutSeconds);
    private MemoryStream _wire = null!;
    private PacketStream _stream = null!;

    [Params(80, 300)]
    public int FrameBytes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _wire = new MemoryStream();
        byte[] payload = new byte[FrameBytes];
        for (int frame = 0; frame < Frames; frame++)
        {
            for (int length = FrameBytes; ;)
            {
                byte low = (byte)(length & 0x7F);
                length >>= 7;
                if (length == 0)
                {
                    _wire.WriteByte(low);
                    break;
                }

                _wire.WriteByte((byte)(low | 0x80));
            }

            _wire.Write(payload);
        }

        _stream = new PacketStream(_wire);
    }

    [Benchmark(OperationsPerInvoke = Frames)]
    public async Task<long> ReadFrames()
    {
        _wire.Position = 0;
        long bytes = 0;
        await foreach (ReadOnlyMemory<byte> frame in _stream.EnumerateRawFramesAsync(ReadBuffer, MaxFrame, s_frameTimeout,
                           TimeProvider.System))
        {
            bytes += frame.Length;
        }

        return bytes;
    }

    [GlobalCleanup]
    public void Cleanup() => _wire.Dispose();
}
