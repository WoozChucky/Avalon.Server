namespace Avalon.World.Testing;

/// <summary>
/// The calling thread's allocated bytes, read so that a test can require a stretch of calls to allocate nothing.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="GC.GetAllocatedBytesForCurrentThread" /> is what the thread was handed minus what is left of its
/// allocation buffer, which the collector hands out about 8 KB at a time. A background collection, in the pause that
/// ends its marking, takes every thread's buffer away without subtracting what was left of it: a thread that allocated
/// nothing then reads up to about 8 KB more, once, with no collection counted in between (#953: 7,816 B in CI and
/// 7,920 B locally over calls that allocate nothing; docs/benchmarks.md, "Allocation tests and background
/// collections"). A blocking collection takes the buffers away too, and does subtract.
/// </para>
/// <para>
/// So a blocking collection goes first. The thread then holds no buffer, and stays without one for as long as it
/// allocates nothing, so a background collection during the window finds nothing of this thread's to count. The window
/// reads what the code under test allocated and nothing else: one object in a thousand calls still shows.
/// </para>
/// <para>
/// This is for a window that must read zero. One that allocates a known amount takes a new buffer with its first
/// object, and a background collection can count that one's rest: such a test takes the fewest bytes of three
/// windows instead (<c>WaypointRepathAllocationShould</c>), which a collection landing in one of them cannot move.
/// </para>
/// </remarks>
public static class ThreadAllocations
{
    /// <summary>
    /// Gives the calling thread's allocation buffer back, then answers <see cref="GC.GetAllocatedBytesForCurrentThread" />.
    /// Nothing may allocate on this thread between this call and the first call of the window.
    /// </summary>
    public static long BeforeQuietWindow()
    {
        GC.Collect(0, GCCollectionMode.Forced, blocking: true);
        return GC.GetAllocatedBytesForCurrentThread();
    }
}
