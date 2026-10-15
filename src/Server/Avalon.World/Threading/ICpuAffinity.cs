namespace Avalon.World.Threading;

/// <summary>
/// The operating system's view of which CPUs each thread of this process may run on (#1002). The world uses
/// <see cref="LinuxCpuAffinity" />; tests substitute it.
/// </summary>
public interface ICpuAffinity
{
    /// <summary>Whether threads can be placed at all: Linux only.</summary>
    bool IsSupported { get; }

    /// <summary>The process id, which is also the id of its main thread.</summary>
    int ProcessId { get; }

    /// <summary>The kernel's id of the calling thread.</summary>
    int CurrentThreadId();

    /// <summary>The kernel's ids of every thread of the process now.</summary>
    IReadOnlyList<int> ThreadIds();

    /// <summary>The CPUs thread <paramref name="threadId" /> may run on; null when it has ended or cannot be read.</summary>
    IReadOnlySet<int>? GetAffinity(int threadId);

    /// <summary>Sets the CPUs thread <paramref name="threadId" /> may run on: 0, or the error number of the failure.</summary>
    int SetAffinity(int threadId, IReadOnlySet<int> cpus);

    /// <summary>The CPUs that share <paramref name="cpu" />'s core, itself included; null when the topology cannot be read.</summary>
    IReadOnlySet<int>? CoreSiblings(int cpu);

    /// <summary>The CPU the calling thread runs on now, or -1 when it cannot be read.</summary>
    int CurrentCpu();
}
