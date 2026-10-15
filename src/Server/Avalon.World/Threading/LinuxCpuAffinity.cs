using System.Globalization;
using System.Runtime.InteropServices;

namespace Avalon.World.Threading;

/// <summary>
/// <see cref="ICpuAffinity" /> over Linux's <c>sched_getaffinity</c>, <c>sched_setaffinity</c> and <c>sched_getcpu</c>,
/// <c>/proc/self/task</c> and the CPU topology under <c>/sys</c> (#1002). Any thread of the process may be read or set by
/// its id, which needs no privilege. On any other system <see cref="IsSupported" /> is false and nothing here is called.
/// </summary>
public sealed partial class LinuxCpuAffinity : ICpuAffinity
{
    /// <summary>The CPUs a mask can name: 1,024, as glibc's <c>cpu_set_t</c>.</summary>
    public const int MaxCpus = MaskWords * 64;

    private const int MaskWords = 16;

    public static readonly LinuxCpuAffinity Instance = new();

    private LinuxCpuAffinity()
    {
    }

    public bool IsSupported => OperatingSystem.IsLinux();

    public int ProcessId => Environment.ProcessId;

    public int CurrentThreadId()
    {
        // "<pid>/task/<tid>": the calling thread's entry, without depending on the C library's gettid.
        string target = new DirectoryInfo("/proc/thread-self").LinkTarget
            ?? throw new IOException("/proc/thread-self is not a link");
        return int.Parse(target[(target.LastIndexOf('/') + 1)..], NumberStyles.None, CultureInfo.InvariantCulture);
    }

    public IReadOnlyList<int> ThreadIds()
    {
        var ids = new List<int>();
        foreach (string directory in Directory.EnumerateDirectories("/proc/self/task"))
        {
            if (int.TryParse(Path.GetFileName(directory), NumberStyles.None, CultureInfo.InvariantCulture, out int id))
                ids.Add(id);
        }

        return ids;
    }

    public unsafe IReadOnlySet<int>? GetAffinity(int threadId)
    {
        ulong* mask = stackalloc ulong[MaskWords];
        if (SchedGetAffinity(threadId, MaskWords * sizeof(ulong), mask) != 0)
            return null;

        var cpus = new SortedSet<int>();
        for (int cpu = 0; cpu < MaxCpus; cpu++)
        {
            if ((mask[cpu / 64] & (1UL << (cpu % 64))) != 0)
                cpus.Add(cpu);
        }

        return cpus;
    }

    public unsafe int SetAffinity(int threadId, IReadOnlySet<int> cpus)
    {
        ulong* mask = stackalloc ulong[MaskWords];
        new Span<ulong>(mask, MaskWords).Clear();
        foreach (int cpu in cpus)
        {
            if (cpu is < 0 or >= MaxCpus)
                return Einval;

            mask[cpu / 64] |= 1UL << (cpu % 64);
        }

        return SchedSetAffinity(threadId, MaskWords * sizeof(ulong), mask) == 0 ? 0 : Marshal.GetLastPInvokeError();
    }

    public IReadOnlySet<int>? CoreSiblings(int cpu)
    {
        string path = $"/sys/devices/system/cpu/cpu{cpu.ToString(CultureInfo.InvariantCulture)}/topology/thread_siblings_list";
        try
        {
            return CpuList.TryParse(File.ReadAllText(path), out IReadOnlySet<int> siblings) ? siblings : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public int CurrentCpu() => SchedGetCpu();

    private const int Einval = 22;

    [LibraryImport("libc", EntryPoint = "sched_getaffinity", SetLastError = true)]
    private static unsafe partial int SchedGetAffinity(int pid, nuint size, ulong* mask);

    [LibraryImport("libc", EntryPoint = "sched_setaffinity", SetLastError = true)]
    private static unsafe partial int SchedSetAffinity(int pid, nuint size, ulong* mask);

    [LibraryImport("libc", EntryPoint = "sched_getcpu")]
    private static partial int SchedGetCpu();
}
