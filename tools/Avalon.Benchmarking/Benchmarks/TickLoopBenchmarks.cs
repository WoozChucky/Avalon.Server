using BenchmarkDotNet.Attributes;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// Measures the per-tick scheduling overhead of the WorldServer throttle mechanism.
///
/// WorldServer.Tick() fills the remaining frame budget by looping:
///   - await Task.Yield()   — when &gt;1 ms remains
///   - Thread.SpinWait(1)   — when &lt;1 ms remains
///
/// At 60 Hz with a ~3 ms tick, ~13 ms remain per frame → ~13 Task.Yield() calls.
/// Each call queues a ThreadPool continuation and may resume on a different CPU core.
///
/// At target scale (250 instances × 60 TPS): ~780 ThreadPool items/s for scheduling alone.
///
/// Scenarios:
///   Yield_1          — baseline: one thread-pool hop (equivalent to a PeriodicTimer-based loop)
///   Yield_5          — fast tick (~11 ms wait)
///   Yield_13         — typical at target load (~3 ms tick + ~13 ms wait)
///   Yield_20         — idle server, sub-ms tick (~16 ms wait)
///   SynchronousPath  — ValueTask.CompletedTask: state-machine cost only, zero scheduling
///
/// Run: dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*TickLoop*"
/// </summary>
[MemoryDiagnoser]
public class TickLoopBenchmarks
{
    /// <summary>
    /// One thread-pool hop — the irreducible minimum cost per tick frame.
    /// A PeriodicTimer-based loop approaches this: one WaitForNextTickAsync() call per tick
    /// instead of N Task.Yield() calls. Used as the baseline.
    /// </summary>
    [Benchmark(Baseline = true)]
    public async Task Yield_1()
    {
        await Task.Yield();
    }

    /// <summary>Fast tick at low load (~11 ms wait, 5 yields).</summary>
    [Benchmark]
    public async Task Yield_5()
    {
        for (int i = 0; i < 5; i++) await Task.Yield();
    }

    /// <summary>
    /// Typical tick at target load: ~3 ms tick + ~13 ms wait = 13 yields per frame.
    /// This is the current approach's scheduling cost at 250 concurrent instances.
    /// </summary>
    [Benchmark]
    public async Task Yield_13()
    {
        for (int i = 0; i < 13; i++) await Task.Yield();
    }

    /// <summary>Idle server, sub-ms tick: full 16 ms budget filled by ~20 yields.</summary>
    [Benchmark]
    public async Task Yield_20()
    {
        for (int i = 0; i < 20; i++) await Task.Yield();
    }

    /// <summary>
    /// Synchronous completion path — the async state machine cost alone, no scheduling.
    /// Represents the floor: what tick overhead would look like if scheduling were free.
    /// This fires when the tick is overdue (diff &gt;= MinUpdateInterval) and skips the throttle.
    /// </summary>
    [Benchmark]
    public async ValueTask SynchronousPath()
    {
        await ValueTask.CompletedTask;
    }
}
