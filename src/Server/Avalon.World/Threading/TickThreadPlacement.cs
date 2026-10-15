using Microsoft.Extensions.Logging;

namespace Avalon.World.Threading;

/// <summary>
/// <c>Game:TickThreadCpu</c> (#1002): on Linux, the world's tick thread runs on that CPU alone, and every other thread of
/// the world stays off it and off the CPUs that share its core (its hyperthread siblings). Unset, nothing here runs.
/// </summary>
/// <remarks>
/// <para>
/// A thread Linux creates starts on its creator's CPUs, but the .NET runtime moves every thread it starts (the thread
/// pool's, the send threads, timers, its own) onto the CPUs of the process's main thread as the thread begins. So the
/// main thread is restricted first, and from then on no thread the runtime starts lands on a reserved CPU, whichever
/// thread asked for it: the tick's own requests to the thread pool included. The threads that already exist are moved
/// off one by one. A thread bound to reserved CPUs alone is left where it is and counted: server GC binds one heap
/// thread to each CPU unless <c>DOTNET_GCHeapAffinitizeRanges</c> leaves the reserved ones out, which the chart sets.
/// </para>
/// <para>
/// All of it runs once, on the tick thread as it starts, before its first tick. A setting that cannot work on this
/// machine (a CPU the process may not use, or one whose core leaves no CPU for the other threads) stops the start
/// (<see cref="Refusal" />); on another system, or when a call fails, one warning is logged and the tick runs unpinned.
/// </para>
/// </remarks>
public static class TickThreadPlacement
{
    /// <summary>The largest CPU number the setting accepts: the last a mask can name.</summary>
    public const int MaxCpu = LinuxCpuAffinity.MaxCpus - 1;

    private const int NoSuchThread = 3; // ESRCH: the thread ended between the listing and the call.

    /// <summary>The most passes over the threads: each after the first only finds a thread that started meanwhile.</summary>
    private const int MaxPasses = 5;

    /// <summary>
    /// Why <paramref name="cpu" /> cannot be the tick's CPU on this machine, or null when it can, or when this system
    /// cannot place threads (the tick then runs unpinned, with a warning, rather than the start failing).
    /// </summary>
    public static string? Refusal(int cpu, ICpuAffinity os)
    {
        if (!os.IsSupported)
            return null;

        IReadOnlySet<int>? allowed = os.GetAffinity(os.ProcessId);
        if (allowed is null)
            return null;

        TryPlan(cpu, allowed, os.CoreSiblings(cpu), out _, out _, out string? refusal);
        return refusal;
    }

    /// <summary>
    /// Pins the calling thread, the tick, to <paramref name="cpu" /> and moves the process's other threads off its core.
    /// Null does nothing at all. True when the tick is pinned.
    /// </summary>
    public static bool PinCurrentThread(int? cpu, ICpuAffinity os, ILogger logger)
    {
        if (cpu is not int target)
            return false;

        if (!os.IsSupported)
        {
            logger.LogWarning(
                "Game:TickThreadCpu is {Cpu}, but the tick thread can be pinned on Linux only; it runs unpinned", target);
            return false;
        }

        try
        {
            return Pin(target, os, logger);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "The tick thread could not be pinned to CPU {Cpu}; it runs unpinned", target);
            return false;
        }
    }

    private static bool Pin(int target, ICpuAffinity os, ILogger logger)
    {
        int main = os.ProcessId;
        IReadOnlySet<int>? allowed = os.GetAffinity(main);
        if (allowed is null)
        {
            logger.LogWarning("The tick thread was not pinned to CPU {Cpu}: the process's CPUs could not be read; it runs unpinned", target);
            return false;
        }

        IReadOnlySet<int>? siblings = os.CoreSiblings(target);
        if (!TryPlan(target, allowed, siblings, out IReadOnlySet<int> reserved, out IReadOnlySet<int> others,
                out string? refusal))
        {
            logger.LogWarning("The tick thread was not pinned: {Refusal}; it runs unpinned", refusal);
            return false;
        }

        if (siblings is null)
        {
            logger.LogWarning(
                "The hyperthread siblings of CPU {Cpu} could not be read; only that CPU is kept free of the world's other threads",
                target);
        }

        // Read before anything changes, so a failure to read it leaves every thread as it was.
        int tick = os.CurrentThreadId();
        if (tick == main)
        {
            // Every thread the runtime starts would follow it onto the one CPU.
            logger.LogWarning("The tick thread was not pinned to CPU {Cpu}: it is the process's main thread; it runs unpinned", target);
            return false;
        }

        // The main thread first: every thread the runtime starts from here copies its CPUs.
        int error = os.SetAffinity(main, others);
        if (error != 0)
        {
            logger.LogWarning("The tick thread was not pinned to CPU {Cpu}: the main thread's CPUs could not be set (error {Error}); it runs unpinned",
                target, error);
            return false;
        }

        error = os.SetAffinity(tick, new SortedSet<int> { target });
        if (error != 0)
        {
            os.SetAffinity(main, allowed);
            logger.LogWarning("The tick thread could not be pinned to CPU {Cpu} (error {Error}); it runs unpinned", target, error);
            return false;
        }

        // The tick is pinned from here: a failure below leaves it pinned, and says so.
        var bound = new HashSet<int>();
        int moved = 0;
        int failed = 0;
        bool settled = false;
        try
        {
            // Until a pass moves nothing: a thread the runtime was starting during a pass may have read the main
            // thread's CPUs before they changed and applied them after the pass had moved it.
            for (int pass = 0; pass < MaxPasses && !settled; pass++)
            {
                int movedBefore = moved;
                foreach (int thread in os.ThreadIds())
                {
                    if (thread == tick || thread == main)
                        continue;

                    IReadOnlySet<int>? cpus = os.GetAffinity(thread);
                    if (cpus is null || !cpus.Overlaps(reserved))
                        continue;

                    var rest = new SortedSet<int>(cpus);
                    rest.ExceptWith(reserved);
                    if (rest.Count == 0)
                    {
                        bound.Add(thread);
                        continue;
                    }

                    error = os.SetAffinity(thread, rest);
                    if (error == 0)
                        moved++;
                    else if (error != NoSuchThread)
                        failed++;
                }

                settled = moved == movedBefore;
            }
        }
        catch (Exception e)
        {
            logger.LogWarning(e,
                "The tick thread is pinned to CPU {Cpu}, but the world's other threads were not all moved off CPUs {Reserved}",
                target, CpuList.Format(reserved));
            return true;
        }

        logger.LogInformation(
            "The tick thread is pinned to CPU {Cpu} (Game:TickThreadCpu) and runs on CPU {Running}; the world's other threads run on CPUs {Others}, off {Reserved}, {Moved} of them moved",
            target, RunningCpu(os), CpuList.Format(others), CpuList.Format(reserved), moved);
        if (bound.Count > 0)
        {
            logger.LogWarning(
                "{Count} threads are bound to CPUs {Reserved} alone and stay there: server GC's heap threads, unless DOTNET_GCHeapAffinitizeRanges leaves those CPUs out (for example {Others})",
                bound.Count, CpuList.Format(reserved), CpuList.Format(others));
        }

        if (failed > 0)
            logger.LogWarning("{Count} threads could not be moved off CPUs {Reserved}", failed, CpuList.Format(reserved));

        if (!settled)
        {
            logger.LogWarning(
                "Threads were still being moved off CPUs {Reserved} after {Passes} passes; some may still run there",
                CpuList.Format(reserved), MaxPasses);
        }

        return true;
    }

    /// <summary>The CPU the calling thread runs on, or -1 when it cannot be read: only the log line reads it.</summary>
    private static int RunningCpu(ICpuAffinity os)
    {
        try
        {
            return os.CurrentCpu();
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return -1;
        }
    }

    /// <summary>
    /// The CPUs the tick reserves (<paramref name="cpu" /> and the siblings the process may use) and those left to every
    /// other thread; false, with the reason, when <paramref name="cpu" /> is not one the process may use or its core is
    /// all the process has. Siblings that cannot be read count as none.
    /// </summary>
    private static bool TryPlan(int cpu, IReadOnlySet<int> allowed, IReadOnlySet<int>? siblings, out IReadOnlySet<int> reserved,
        out IReadOnlySet<int> others, out string? refusal)
    {
        var core = new SortedSet<int>(siblings ?? Enumerable.Empty<int>()) { cpu };
        core.IntersectWith(allowed);
        var rest = new SortedSet<int>(allowed);
        rest.ExceptWith(core);
        reserved = core;
        others = rest;
        refusal = null;

        if (!allowed.Contains(cpu))
            refusal = $"Game:TickThreadCpu {cpu} is not one of the CPUs this process may run on ({CpuList.Format(allowed)})";
        else if (rest.Count == 0)
            refusal = $"Game:TickThreadCpu {cpu} and its core's other CPUs ({CpuList.Format(core)}) are every CPU this process may run on, which leaves none to the world's other threads";

        return refusal is null;
    }
}
