using System.Runtime.CompilerServices;
using Avalon.Network.Packets.Party;
using Avalon.World.Configuration;
using Avalon.World.Parties;
using Avalon.World.Persistence;
using Avalon.World.Social;
using Avalon.World.Threading;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// The cost of <see cref="TickThreadGuard" /> (#639) while it is switched off, the production default. The switch can
/// only be turned on in a process, so the on state is measured by <see cref="TickThreadGuardOnBenchmarks" />; this
/// relies on BenchmarkDotNet's default out-of-process toolchain, which runs every benchmark in a process of its own,
/// and refuses to run if the guard is somehow already on.
/// </summary>
[MemoryDiagnoser]
public class TickThreadGuardOffBenchmarks
{
    private TickThreadGuardFixture _fixture = null!;

    [GlobalSetup]
    public void Setup()
    {
        if (TickThreadGuard.Enabled)
            throw new InvalidOperationException("The guard is on in this process; run out of process (the default).");

        _fixture = new TickThreadGuardFixture();
    }

    /// <summary>An empty method of the guard's shape: the floor every guarded call is measured against.</summary>
    [Benchmark(Baseline = true)]
    public void Baseline_EmptyCall() => _fixture.Empty?.AssertOnTick("World.TransferPlayer");

    [Benchmark]
    public void Guard_Off() => _fixture.Guard?.AssertOnTick("World.TransferPlayer");

    /// <summary>A real guarded write with no guard handed over, against the same write with the guard off.</summary>
    [Benchmark]
    public bool IgnoreList_AddRemove_NoGuard() => _fixture.UnguardedIgnores.AddRemove();

    [Benchmark]
    public bool IgnoreList_AddRemove_GuardOff() => _fixture.GuardedIgnores.AddRemove();

    [Benchmark]
    public PartyResult PartyService_Leave_NoGuard() => _fixture.UnguardedParties.Leave(7);

    [Benchmark]
    public PartyResult PartyService_Leave_GuardOff() => _fixture.GuardedParties.Leave(7);
}

/// <summary>
/// The cost of <see cref="TickThreadGuard" /> switched on and bound, called on the bound thread (the tick in
/// production). Turns the guard on for its own process in <see cref="Setup" />; BenchmarkDotNet runs the global setup and
/// the workload on the same thread, and a call from any other thread would throw, so a result here also proves it ran
/// on the bound thread.
/// </summary>
[MemoryDiagnoser]
public class TickThreadGuardOnBenchmarks
{
    private TickThreadGuardFixture _fixture = null!;

    [GlobalSetup]
    public void Setup()
    {
        TickThreadGuard.Enable();
        _fixture = new TickThreadGuardFixture();
        _fixture.Guard.Bind();
    }

    [Benchmark(Baseline = true)]
    public void Baseline_EmptyCall() => _fixture.Empty?.AssertOnTick("World.TransferPlayer");

    [Benchmark]
    public void Guard_OnBound() => _fixture.Guard?.AssertOnTick("World.TransferPlayer");

    [Benchmark]
    public bool IgnoreList_AddRemove_GuardOn() => _fixture.GuardedIgnores.AddRemove();

    [Benchmark]
    public PartyResult PartyService_Leave_GuardOn() => _fixture.GuardedParties.Leave(7);
}

internal sealed class TickThreadGuardFixture
{
    public TickThreadGuard Guard { get; } = new();
    public EmptyGuard? Empty { get; } = new();
    public IgnoreRoundTrip UnguardedIgnores { get; } = new(null);
    public IgnoreRoundTrip GuardedIgnores { get; }
    public PartyService UnguardedParties { get; }
    public PartyService GuardedParties { get; }

    public TickThreadGuardFixture()
    {
        GuardedIgnores = new IgnoreRoundTrip(Guard);
        UnguardedParties = Parties(null);
        GuardedParties = Parties(Guard);
    }

    private static PartyService Parties(TickThreadGuard? guard) =>
        new(Options.Create(new GameConfiguration()), TimeProvider.System, NullLogger<PartyService>.Instance,
            tickThread: guard);
}

/// <summary>The guard's shape with an empty body: what a call costs before the guard does anything.</summary>
internal sealed class EmptyGuard
{
    public void AssertOnTick(string operation)
    {
    }
}

/// <summary>One ignore and one unignore, the list ending as it began.</summary>
internal sealed class IgnoreRoundTrip(TickThreadGuard? guard)
{
    private readonly IgnoreList _list = new(new SaveStateTracker(), guard);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool AddRemove() => _list.Add(2, "Borin", DateTime.UnixEpoch) & _list.Remove(2);
}
