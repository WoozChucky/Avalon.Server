using Avalon.World.Configuration;
using Avalon.World.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Avalon.Server.World.UnitTests.Threading;

/// <summary>
/// #1002: <c>Game:TickThreadCpu</c> pins the tick thread to one CPU and keeps every other thread of the world off that
/// CPU's core. Driven over a machine described in memory (<see cref="FakeCpus" />), so every system runs these; the real
/// Linux calls were checked once in a Linux container (docs/world-simulation.md, "Tick thread placement").
/// </summary>
public class TickThreadPlacementShould
{
    private const int Main = 100;
    private const int Tick = 101;
    private const int Pool = 102;
    private const int GcOnTickCpu = 103;
    private const int GcOnSibling = 104;
    private const int GcElsewhere = 105;
    private const int AlreadyOff = 106;
    private const int Late = 107;

    [Theory]
    [InlineData("0-23", "8,20", 8, null)]
    [InlineData("0-23", null, 8, null)]
    [InlineData("0-7", "8,20", 8, "is not one of the CPUs this process may run on (0-7)")]
    [InlineData("8,20", "8,20", 8, "leaves none to the world's other threads")]
    public void Refuse_at_startup_a_cpu_this_machine_cannot_give_the_tick(string allowed, string? siblings, int cpu,
        string? refused)
    {
        var os = new FakeCpus(siblings);
        os.Masks[Main] = Cpus(allowed);

        ValidateOptionsResult result =
            new TickThreadCpuValidation(os).Validate(null, new GameConfiguration { TickThreadCpu = cpu });

        if (refused is null)
        {
            Assert.True(result.Succeeded);
        }
        else
        {
            Assert.True(result.Failed);
            Assert.Contains(refused, result.FailureMessage, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Unset, nothing is called and nothing is logged. Off Linux, or when the main thread's or the tick's own CPUs cannot
    /// be set, one warning, and every thread keeps the CPUs it had: the tick runs unpinned rather than half-placed.
    /// </summary>
    [Theory]
    [InlineData("unset", 0)]
    [InlineData("not linux", 1)]
    [InlineData("main thread refused", 1)]
    [InlineData("tick refused", 1)]
    public void Leave_every_thread_where_it_was_when_unset_or_when_pinning_fails(string situation, int warnings)
    {
        var os = new FakeCpus("8,20")
        {
            IsSupported = situation != "not linux",
            Refuses = situation switch
            {
                "main thread refused" => Main,
                "tick refused" => Tick,
                _ => 0,
            },
        };
        Dictionary<int, IReadOnlySet<int>> before = Machine(os);
        var logger = new RecordingLogger();

        bool pinned = TickThreadPlacement.PinCurrentThread(situation == "unset" ? null : 8, os, logger);

        Assert.False(pinned);
        foreach ((int thread, IReadOnlySet<int> cpus) in before)
            Assert.True(cpus.SetEquals(os.Masks[thread]), $"thread {thread} was moved");
        if (situation is "unset" or "not linux")
            Assert.Empty(os.Sets);
        Assert.Equal(warnings, logger.Entries.Count);
        Assert.All(logger.Entries, entry => Assert.Equal(LogLevel.Warning, entry.Level));
    }

    /// <summary>
    /// The main thread is restricted before anything else, since the runtime starts every new thread on its CPUs; then
    /// the tick goes onto its CPU and every existing thread off the core. A thread bound to the core alone (a server GC
    /// heap thread without DOTNET_GCHeapAffinitizeRanges) stays, with a warning; one already off it is not touched.
    /// </summary>
    [Fact]
    public void Pin_the_tick_and_move_every_other_thread_off_its_core()
    {
        var os = new FakeCpus("8,20");
        Machine(os);
        var logger = new RecordingLogger();

        bool pinned = TickThreadPlacement.PinCurrentThread(8, os, logger);

        Assert.True(pinned);
        Assert.Equal([Main, Tick, Pool], os.Sets);
        IReadOnlySet<int> others = Cpus("0-7,9-19,21-23");
        Assert.True(os.Masks[Main].SetEquals(others));
        Assert.True(os.Masks[Tick].SetEquals([8]));
        Assert.True(os.Masks[Pool].SetEquals(others));
        Assert.True(os.Masks[GcOnTickCpu].SetEquals([8]));
        Assert.True(os.Masks[GcOnSibling].SetEquals([20]));
        Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    /// <summary>
    /// Once the tick is on its CPU it stays pinned, whatever happens to the rest, and what could not be done is one
    /// warning: the threads could not be listed; a thread that took the main thread's old CPUs back after being moved,
    /// twice (moved again until a pass moves nothing) or every time (the passes give up, with a warning).
    /// </summary>
    [Theory]
    [InlineData("thread list fails", 1)]
    [InlineData("late thread reverts twice", 0)]
    [InlineData("late thread always reverts", 1)]
    public void Keep_the_tick_pinned_and_warn_when_the_rest_is_incomplete(string situation, int warnings)
    {
        var os = new FakeCpus("8,20")
        {
            ListFails = situation == "thread list fails",
            LateReverts = situation switch
            {
                "late thread reverts twice" => 2,
                "late thread always reverts" => int.MaxValue,
                _ => 0,
            },
        };
        os.Masks[Main] = Cpus("0-23");
        os.Masks[Tick] = Cpus("0-23");
        os.Masks[Pool] = Cpus("0-23");
        os.Masks[Late] = Cpus("0-23");
        var logger = new RecordingLogger();

        bool pinned = TickThreadPlacement.PinCurrentThread(8, os, logger);

        Assert.True(pinned);
        Assert.True(os.Masks[Tick].SetEquals([8]));
        Assert.Equal(warnings, logger.Entries.Count(entry => entry.Level == LogLevel.Warning));
        if (situation == "late thread reverts twice")
            Assert.False(os.Masks[Late].Overlaps([8, 20]));
    }

    /// <summary>A world node: 24 CPUs, the main thread, the tick, a pool thread, three GC heap threads, one thread already off.</summary>
    private static Dictionary<int, IReadOnlySet<int>> Machine(FakeCpus os)
    {
        os.Masks[Main] = Cpus("0-23");
        os.Masks[Tick] = Cpus("0-23");
        os.Masks[Pool] = Cpus("0-23");
        os.Masks[GcOnTickCpu] = Cpus("8");
        os.Masks[GcOnSibling] = Cpus("20");
        os.Masks[GcElsewhere] = Cpus("3");
        os.Masks[AlreadyOff] = Cpus("0-7");
        return new Dictionary<int, IReadOnlySet<int>>(os.Masks);
    }

    private static IReadOnlySet<int> Cpus(string list)
    {
        Assert.True(CpuList.TryParse(list, out IReadOnlySet<int> cpus));
        return cpus;
    }

    /// <summary>Threads and their CPUs in memory; <see cref="Refuses" /> names a thread whose CPUs cannot be set.</summary>
    private sealed class FakeCpus(string? siblings) : ICpuAffinity
    {
        public Dictionary<int, IReadOnlySet<int>> Masks { get; } = [];

        /// <summary>The threads whose CPUs were set, in order.</summary>
        public List<int> Sets { get; } = [];

        public bool IsSupported { get; init; } = true;

        public int Refuses { get; init; }

        /// <summary>Listing the threads throws, as reading <c>/proc/self/task</c> can.</summary>
        public bool ListFails { get; init; }

        /// <summary>
        /// How many times thread <see cref="Late" /> takes its old CPUs back after being moved: a thread the runtime was
        /// starting, that read the main thread's CPUs before they changed and applied them after the move.
        /// </summary>
        public int LateReverts { get; set; }

        public int ProcessId => Main;

        public int CurrentThreadId() => Tick;

        public IReadOnlyList<int> ThreadIds() =>
            ListFails ? throw new IOException("/proc/self/task could not be read") : Masks.Keys.ToList();

        public IReadOnlySet<int>? GetAffinity(int threadId) => Masks.GetValueOrDefault(threadId);

        public int SetAffinity(int threadId, IReadOnlySet<int> cpus)
        {
            if (threadId == Refuses)
                return 1;

            if (threadId == Late && LateReverts > 0)
            {
                LateReverts--;
                return 0;
            }

            Sets.Add(threadId);
            Masks[threadId] = new SortedSet<int>(cpus);
            return 0;
        }

        public IReadOnlySet<int>? CoreSiblings(int cpu) => siblings is null ? null : Cpus(siblings);

        public int CurrentCpu() => Masks[Tick].Count == 1 ? Masks[Tick].Single() : -1;
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
    }
}
