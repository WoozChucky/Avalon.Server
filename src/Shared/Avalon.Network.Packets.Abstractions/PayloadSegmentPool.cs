namespace Avalon.Network.Packets.Abstractions;

/// <summary>
/// Free payload segments by size class, powers of two from 64 B to 1 MiB (#875). Unbounded on purpose: it holds what
/// was in flight at the peak, which the send path caps per connection (<c>Network:MaxPendingBytes</c>, past which the
/// connection is closed as too slow), and a pool that dropped segments would make the tick allocate new ones. A payload
/// above 1 MiB gets a segment of its own, never pooled.
/// </summary>
/// <remarks>
/// Each size class is a stack under a lock, which allocates only at a new peak. A <c>ConcurrentQueue</c> did not: after
/// a burst, it kept allocating new, larger internal segments with no new peak until its free segments sat in one; those
/// allocations landed in later ticks. The tick rents and the send threads return, so a size class's lock can be
/// contended (uncontended it costs about 16 ns more per packet than the queue; contention is measured under #883); a
/// lock-free stack would allocate a node per push.
/// </remarks>
public sealed class PayloadSegmentPool
{
    private const int SmallestShift = 6;
    private const int LargestShift = 20;

    private readonly Stack<PayloadSegment>[] _free = new Stack<PayloadSegment>[LargestShift - SmallestShift + 1];

    private readonly bool _countsOutstanding;
    private long _outstanding;

    /// <param name="countOutstanding">
    /// Counts the segments rented and not yet returned (<see cref="Outstanding" />): a test's pool. Off for the process's
    /// pool, whose counter would be one shared word written by the tick and every send thread twice per packet (#883).
    /// </param>
    public PayloadSegmentPool(bool countOutstanding = false)
    {
        _countsOutstanding = countOutstanding;
        for (int i = 0; i < _free.Length; i++)
            _free[i] = new Stack<PayloadSegment>();
    }

    /// <summary>The process's pool, which <c>PacketEncoder.Shared</c> encodes into. It counts nothing.</summary>
    public static PayloadSegmentPool Shared { get; } = new();

    /// <summary>
    /// Segments rented and not yet returned: 0 once every packet encoded has been written or released. Only a pool made
    /// to count them has the figure; any other throws rather than read a 0 it never counted.
    /// </summary>
    public long Outstanding => _countsOutstanding
        ? Interlocked.Read(ref _outstanding)
        : throw new InvalidOperationException("This payload segment pool does not count its outstanding segments.");

    /// <summary>A segment holding a copy of <paramref name="payload" />, with one reference.</summary>
    public PayloadSegment Rent(ReadOnlySpan<byte> payload)
    {
        int sizeClass = SizeClassOf(payload.Length);
        PayloadSegment? segment = null;
        if (sizeClass >= 0)
        {
            Stack<PayloadSegment> free = _free[sizeClass];
            lock (free)
                free.TryPop(out segment);
        }

        segment ??= new PayloadSegment(this, sizeClass,
            sizeClass >= 0 ? 1 << (sizeClass + SmallestShift) : payload.Length);
        segment.Fill(payload);
        if (_countsOutstanding)
            Interlocked.Increment(ref _outstanding);
        return segment;
    }

    internal void Return(PayloadSegment segment)
    {
        if (_countsOutstanding)
            Interlocked.Decrement(ref _outstanding);
        if (segment.SizeClass >= 0)
        {
            Stack<PayloadSegment> free = _free[segment.SizeClass];
            lock (free)
                free.Push(segment);
        }
    }

    private static int SizeClassOf(int length)
    {
        int shift = SmallestShift;
        while ((1 << shift) < length)
        {
            if (++shift > LargestShift)
                return -1;
        }

        return shift - SmallestShift;
    }
}
